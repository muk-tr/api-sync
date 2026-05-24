using System.Xml.Linq;

namespace api_sync.Utilities;

public record DetectedProject(string Name, bool IsWebProject, string CsprojPath)
{
    // "Weare.Mighty.Alright" → "Alright"
    public string CollectionName => Name.Contains('.')
        ? Name.Split('.').Last()
        : Name;

    public string? DefaultAssemblyPath
    {
        get
        {
            var framework = ReadTargetFramework();
            if (framework is null) return null;

            var projectDir = Path.GetDirectoryName(CsprojPath)!;
            return Path.Combine(projectDir, "bin", "Debug", framework, $"{Name}.dll");
        }
    }

    private string? ReadTargetFramework()
    {
        try
        {
            var doc = XDocument.Load(CsprojPath);
            return doc.Descendants("TargetFramework").FirstOrDefault()?.Value;
        }
        catch
        {
            return null;
        }
    }
}

public static class ProjectDetector
{
    public static List<DetectedProject> FindProjects(string searchRoot)
    {
        return Directory
            .EnumerateFiles(searchRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => Depth(searchRoot, path) <= 2)
            .Select(path => new DetectedProject(
                Name: Path.GetFileNameWithoutExtension(path),
                IsWebProject: IsWebProject(path),
                CsprojPath: path
            ))
            .Where(p => p.IsWebProject)
            .ToList();
    }

    // ASP.NET Core projects (APIs, MVC, Blazor) use Microsoft.NET.Sdk.Web.
    // Plain console apps and libraries use Microsoft.NET.Sdk.
    private static bool IsWebProject(string csprojPath)
    {
        try
        {
            var content = File.ReadAllText(csprojPath);
            return content.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // Returns how many directories deep the file is relative to the root
    private static int Depth(string root, string filePath)
    {
        var relative = Path.GetRelativePath(root, filePath);
        return relative.Split(Path.DirectorySeparatorChar).Length - 1;
    }
}
