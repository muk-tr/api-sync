using System.Text;
using api_sync.Config;
using api_sync.Models;

namespace api_sync.Bruno;

public static class BrunoWriter
{
    public static void Write(BrunoProviderConfig provider, List<NormalizedRequest> requests)
    {
        var root = provider.RepoPath;
        Directory.CreateDirectory(root);

        WriteBrunoJson(root, provider.CollectionName);

        var grouped = GroupRequests(requests, provider.GroupBy);
        var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seq = 1;

        foreach (var (folder, folderRequests) in grouped)
        {
            var folderPath = folder.Length == 0 ? root : Path.Combine(root, folder);
            Directory.CreateDirectory(folderPath);

            foreach (var request in folderRequests)
            {
                var fileName = Sanitize(request.Name) + ".bru";
                var filePath = Path.Combine(folderPath, fileName);
                File.WriteAllText(filePath, RenderBru(request, seq++));
                writtenFiles.Add(filePath);
            }
        }

        RemoveStale(root, writtenFiles);
    }

    private static void WriteBrunoJson(string root, string collectionName)
    {
        var content = $$"""
            {
              "version": "1",
              "name": "{{collectionName}}",
              "type": "collection",
              "ignore": []
            }
            """;
        File.WriteAllText(Path.Combine(root, "bruno.json"), content);
    }

    private static Dictionary<string, List<NormalizedRequest>> GroupRequests(
        List<NormalizedRequest> requests, string groupBy)
    {
        var grouped = new Dictionary<string, List<NormalizedRequest>>(StringComparer.OrdinalIgnoreCase);

        foreach (var request in requests)
        {
            var key = groupBy == "tags"
                ? (request.Tags.FirstOrDefault() ?? "")
                : (FirstPathSegment(request.Path) ?? "");

            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped[key] = list;
            }
            list.Add(request);
        }

        return grouped;
    }

    private static string? FirstPathSegment(string path)
    {
        var segment = path.Trim('/').Split('/')[0];
        return segment.Length > 0 ? segment : null;
    }

    private static string RenderBru(NormalizedRequest request, int seq)
    {
        var sb = new StringBuilder();

        sb.AppendLine("meta {");
        sb.AppendLine($"  name: {request.Name}");
        sb.AppendLine("  type: http");
        sb.AppendLine($"  seq: {seq}");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"{request.Method.ToLowerInvariant()} {{");
        sb.AppendLine($"  url: {{{{baseUrl}}}}{request.Path}");
        sb.AppendLine($"  body: {(request.HasBody ? "json" : "none")}");
        sb.AppendLine("  auth: none");
        sb.AppendLine("}");

        if (request.HasBody)
        {
            sb.AppendLine();
            sb.AppendLine("body:json {");
            sb.AppendLine("  {}");
            sb.AppendLine("}");
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static void RemoveStale(string root, HashSet<string> writtenFiles)
    {
        foreach (var file in Directory.GetFiles(root, "*.bru", SearchOption.AllDirectories))
        {
            if (!writtenFiles.Contains(file))
                File.Delete(file);
        }

        // Remove directories that are now empty (deepest first)
        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
    }
}
