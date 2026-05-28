using api_sync.Config;
using api_sync.Models;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace api_sync.Scanner;

public static class OpenApiScanner
{
    public static async Task<List<NormalizedRequest>> ScanAsync(BrunoProviderConfig provider)
    {
        if (provider.AssemblyPath is not null)
            return AssemblyScanner.Scan(provider.AssemblyPath);

        await using var stream = await OpenSpecStreamAsync(provider);

        var reader = new OpenApiStreamReader();
        var doc = reader.Read(stream, out var diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            var messages = string.Join("\n", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"OpenAPI spec has errors:\n{messages}");
        }

        return doc.Paths
            .SelectMany(path => path.Value.Operations.Select(op => ToNormalizedRequest(path.Key, op.Key, op.Value)))
            .ToList();
    }

    private static async Task<Stream> OpenSpecStreamAsync(BrunoProviderConfig provider)
    {
        if (provider.OpenapiPath is not null)
        {
            if (!File.Exists(provider.OpenapiPath))
                throw new FileNotFoundException($"Spec file not found: {provider.OpenapiPath}");

            return File.OpenRead(provider.OpenapiPath);
        }

        if (provider.OpenapiUrl is not null)
        {
            var http = new HttpClient();
            var response = await http.GetAsync(provider.OpenapiUrl);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync();
        }


        throw new InvalidOperationException("No spec source configured for this provider.");
    }

    private static NormalizedRequest ToNormalizedRequest(string path, OperationType operationType, OpenApiOperation operation)
    {
        var method = operationType.ToString().ToUpperInvariant();
        var name = operation.Summary ?? operation.OperationId ?? $"{method} {path}";
        var tags = operation.Tags?.Select(t => t.Name).ToList() ?? [];
        var hasBody = operationType is OperationType.Post or OperationType.Put or OperationType.Patch;

        return new NormalizedRequest(method, path, name, tags, hasBody);
    }
}
