using System.Reflection;
using api_sync.Models;

namespace api_sync.Scanner;

public static class AssemblyScanner
{
    public static List<NormalizedRequest> Scan(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Assembly not found: {fullPath}. Build the project first.");

        var assemblyDir = Path.GetDirectoryName(fullPath)!;
        var resolver = new PathAssemblyResolver(CollectDlls(assemblyDir));
        using var mlc = new MetadataLoadContext(resolver);

        var assembly = mlc.LoadFromAssemblyPath(fullPath);
        var requests = new List<NormalizedRequest>();

        foreach (var type in assembly.GetTypes())
        {
            if (!IsController(type)) continue;

            var controllerRoute = GetRouteTemplate(type);
            var controllerName = GetControllerName(type);

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var httpInfo = GetHttpInfo(method);
                if (httpInfo is null) continue;

                var (httpMethod, actionRoute) = httpInfo.Value;
                var fullRoute = BuildPath(controllerRoute, actionRoute, controllerName);
                var hasBody = httpMethod is "POST" or "PUT" or "PATCH";

                requests.Add(new NormalizedRequest(
                    Method: httpMethod,
                    Path: fullRoute,
                    Name: method.Name,
                    Tags: [controllerName],
                    HasBody: hasBody
                ));
            }
        }

        return requests;
    }

    // Collect dlls from:
    // 1. The .NET runtime (e.g. dotnet/shared/Microsoft.NETCore.App/10.0.x)
    // 2. All other shared frameworks (e.g. Microsoft.AspNetCore.App) — same parent folder
    // 3. The assembly's own output directory (its local dependencies)
    private static string[] CollectDlls(string assemblyDir)
    {
        // Key by filename so the same assembly from different paths is only added once.
        // Add in ascending priority order so later entries win.
        var dlls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(IEnumerable<string> paths)
        {
            foreach (var path in paths)
                dlls[Path.GetFileName(path)] = path;
        }

        // 1. .NET runtime (lowest priority)
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        Add(Directory.GetFiles(runtimeDir, "*.dll"));

        // 2. All shared frameworks (Microsoft.AspNetCore.App, etc.)
        // runtimeDir = .../dotnet/shared/Microsoft.NETCore.App/x.x.x
        // sharedRoot = .../dotnet/shared
        var sharedRoot = Path.GetDirectoryName(Path.GetDirectoryName(runtimeDir));
        if (sharedRoot != null && Directory.Exists(sharedRoot))
            Add(Directory.GetFiles(sharedRoot, "*.dll", SearchOption.AllDirectories));

        // 3. Assembly's own output folder (highest priority — local NuGet deps)
        Add(Directory.GetFiles(assemblyDir, "*.dll"));

        return [.. dlls.Values];
    }

    // A type is a controller if it inherits from ControllerBase or has [ApiController]
    private static bool IsController(Type type)
    {
        if (!type.IsClass || type.IsAbstract || !type.IsPublic) return false;

        if (type.CustomAttributes.Any(a => a.AttributeType.Name == "ApiControllerAttribute"))
            return true;

        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (baseType.Name is "ControllerBase" or "Controller")
                return true;
            baseType = baseType.BaseType;
        }

        return false;
    }

    // Read [Route("template")] from a type or method
    private static string? GetRouteTemplate(MemberInfo member)
    {
        return member.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.Name == "RouteAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value?.ToString();
    }

    // Strip the "Controller" suffix to get the resource name e.g. PaymentsController → Payments
    private static string GetControllerName(Type type)
    {
        var name = type.Name;
        return name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
            ? name[..^"Controller".Length]
            : name;
    }

    // Find the HTTP method attribute and optional route template on an action method
    private static (string Method, string? Route)? GetHttpInfo(MethodInfo method)
    {
        foreach (var attr in method.CustomAttributes)
        {
            var route = attr.ConstructorArguments.Count > 0
                ? attr.ConstructorArguments[0].Value?.ToString()
                : null;

            var httpMethod = attr.AttributeType.Name switch
            {
                "HttpGetAttribute" => "GET",
                "HttpPostAttribute" => "POST",
                "HttpPutAttribute" => "PUT",
                "HttpDeleteAttribute" => "DELETE",
                "HttpPatchAttribute" => "PATCH",
                _ => null
            };

            if (httpMethod is not null)
                return (httpMethod, route);
        }

        return null;
    }

    // Combine controller route + action route, replacing [controller] token
    private static string BuildPath(string? controllerRoute, string? actionRoute, string controllerName)
    {
        var prefix = (controllerRoute ?? "[controller]")
            .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase);

        prefix = "/" + prefix.TrimStart('/');

        if (string.IsNullOrEmpty(actionRoute))
            return prefix;

        return prefix.TrimEnd('/') + "/" + actionRoute.TrimStart('/');
    }
}
