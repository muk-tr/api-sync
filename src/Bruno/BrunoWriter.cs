using System.Text;
using System.Text.RegularExpressions;
using api_sync.Config;
using api_sync.Models;

namespace api_sync.Bruno;

public static class BrunoWriter
{
    public static void Write(BrunoProviderConfig provider, List<NormalizedRequest> requests)
    {
        var root = provider.RepoPath;
        Directory.CreateDirectory(root);

        var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        WriteIfNew(Path.Combine(root, "opencollection.yml"), () => RenderCollection(provider.CollectionName));
        writtenFiles.Add(Path.Combine(root, "opencollection.yml"));

        WriteEnvironments(root, provider);

        // Never delete environment files — they contain user secrets
        var envsDir = Path.Combine(root, "environments");
        if (Directory.Exists(envsDir))
            foreach (var f in Directory.GetFiles(envsDir))
                writtenFiles.Add(f);

        var grouped = GroupRequests(requests, provider.GroupBy);
        var folderSeq = 1;

        foreach (var (folder, folderRequests) in grouped)
        {
            var folderPath = folder.Length == 0 ? root : Path.Combine(root, Sanitize(folder));
            Directory.CreateDirectory(folderPath);

            if (folder.Length > 0)
            {
                var folderFile = Path.Combine(folderPath, "folder.yml");
                WriteIfNew(folderFile, () => RenderFolder(folder, folderSeq++));
                writtenFiles.Add(folderFile);
            }

            var seq = 1;
            foreach (var request in folderRequests)
            {
                var filePath = Path.Combine(folderPath, Sanitize(request.Name) + ".yml");
                WriteOrUpdate(filePath, RenderRequest(request, seq++));
                writtenFiles.Add(filePath);
            }
        }

        RemoveStale(root, writtenFiles);
    }

    private static void WriteIfNew(string path, Func<string> content)
    {
        if (!File.Exists(path))
            File.WriteAllText(path, content());
    }

    // Regenerates info + http + settings, but preserves the runtime block (user scripts).
    private static void WriteOrUpdate(string path, string newContent)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, newContent);
            return;
        }

        var existing = File.ReadAllText(path);
        var runtime = ExtractRuntimeBlock(existing);
        if (runtime is null)
        {
            File.WriteAllText(path, newContent);
            return;
        }

        var settingsIdx = newContent.IndexOf("\nsettings:", StringComparison.Ordinal);
        var merged = settingsIdx < 0
            ? newContent.TrimEnd() + "\n\n" + runtime + "\n"
            : newContent[..(settingsIdx + 1)] + runtime + "\n\n" + newContent[(settingsIdx + 1)..];

        File.WriteAllText(path, merged);
    }

    private static string? ExtractRuntimeBlock(string content)
    {
        var lines = content.Split('\n');
        var start = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd() == "runtime:")
            {
                start = i;
                continue;
            }

            if (start >= 0 && lines[i].Length > 0 && !char.IsWhiteSpace(lines[i][0]))
                return string.Join('\n', lines[start..i]).TrimEnd();
        }

        return start >= 0 ? string.Join('\n', lines[start..]).TrimEnd() : null;
    }

    // $$$: interpolation = {{{expr}}}, so literal {{ and }} can appear as-is (for Bruno's {{token}} syntax)
    private static string RenderCollection(string name) => $$$"""
        opencollection: 1.0.0

        info:
          name: {{{name}}}
        config:
          proxy:
            inherit: true
            config:
              protocol: http
              hostname: ""
              port: ""
              auth:
                username: ""
                password: ""
              bypassProxy: ""

        request:
          auth:
            type: bearer
            token: "{{token}}"
        bundled: false
        extensions:
          bruno:
            ignore:
              - node_modules
              - .git
        """;

    private static void WriteEnvironments(string root, BrunoProviderConfig provider)
    {
        var envsDir = Path.Combine(root, "environments");
        Directory.CreateDirectory(envsDir);

        var localPath = Path.Combine(envsDir, "local.yml");
        if (File.Exists(localPath)) return;

        var baseUrl = provider.OpenapiUrl is not null
            ? new Uri(provider.OpenapiUrl).GetLeftPart(UriPartial.Authority)
            : "https://localhost:5001";

        File.WriteAllText(localPath, $"""
            name: local
            variables:
              - name: baseUrl
                value: {baseUrl}
              - secret: true
                name: token
            """);
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
        foreach (var segment in path.Trim('/').Split('/'))
        {
            if (segment.Length == 0) continue;
            if (IsVersionSegment(segment)) continue;
            return segment;
        }
        return null;
    }

    private static bool IsVersionSegment(string segment) =>
        segment.StartsWith('v') && segment.Length > 1 &&
        (char.IsDigit(segment[1]) || segment.Contains("version", StringComparison.OrdinalIgnoreCase));

    // $$: interpolation = {{expr}}, so literal { and } can appear as-is
    private static string RenderFolder(string name, int seq) => $$"""
        info:
          name: {{name}}
          type: folder
          seq: {{seq}}

        request: {}
        """;

    private static string RenderRequest(NormalizedRequest request, int seq)
    {
        var (url, pathParams) = ExtractPathParams(request.Path);
        var sb = new StringBuilder();

        sb.AppendLine("info:");
        sb.AppendLine($"  name: {request.Name}");
        sb.AppendLine("  type: http");
        sb.AppendLine($"  seq: {seq}");

        if (request.Tags.Count > 0)
        {
            sb.AppendLine("  tags:");
            foreach (var tag in request.Tags)
                sb.AppendLine($"    - {tag}");
        }

        sb.AppendLine();
        sb.AppendLine("http:");
        sb.AppendLine($"  method: {request.Method}");
        sb.AppendLine($"  url: \"{{{{baseUrl}}}}{url}\"");

        if (pathParams.Count > 0)
        {
            sb.AppendLine("  params:");
            foreach (var param in pathParams)
            {
                sb.AppendLine($"    - name: {param}");
                sb.AppendLine("      value: \"\"");
                sb.AppendLine("      type: path");
            }
        }

        if (request.HasBody)
        {
            sb.AppendLine("  body:");
            sb.AppendLine("    type: json");
            sb.AppendLine("    data: |-");
            sb.AppendLine("      {}");
        }

        sb.AppendLine("  auth: inherit");
        sb.AppendLine();
        sb.AppendLine("settings:");
        sb.AppendLine("  encodeUrl: true");
        sb.AppendLine("  timeout: 0");
        sb.AppendLine("  followRedirects: true");
        sb.Append("  maxRedirects: 5");

        return sb.ToString().TrimEnd() + "\n";
    }

    private static (string url, List<string> paramNames) ExtractPathParams(string path)
    {
        var names = new List<string>();
        var converted = Regex.Replace(path, @"\{(\w+)(?::[^}]*)?\}", m =>
        {
            names.Add(m.Groups[1].Value);
            return $":{m.Groups[1].Value}";
        });
        return (converted, names);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static void RemoveStale(string root, HashSet<string> writtenFiles)
    {
        foreach (var file in Directory.GetFiles(root, "*.yml", SearchOption.AllDirectories))
        {
            if (!writtenFiles.Contains(file))
                File.Delete(file);
        }

        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
    }
}
