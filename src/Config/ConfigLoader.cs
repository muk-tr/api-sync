using System.Text.Json;

namespace api_sync.Config;

public static class ConfigLoader
{
    private const string ConfigFileName = ".api-sync.json";

    public static ApiSyncConfig Load(string directory)
    {
        var path = Path.Combine(directory, ConfigFileName);

        if (!File.Exists(path))
            throw new ConfigException($"{ConfigFileName} not found. Run 'api-sync init' to create one.");

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new ConfigException($"Could not read {ConfigFileName}: {ex.Message}");
        }

        ApiSyncConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<ApiSyncConfig>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"{ConfigFileName} contains invalid JSON: {ex.Message}");
        }

        if (config is null)
            throw new ConfigException($"{ConfigFileName} is empty.");

        Validate(config);

        return config;
    }

    private static void Validate(ApiSyncConfig config)
    {
        var errors = new List<string>();

        if (config.Providers.Count == 0)
            errors.Add("No providers configured. Add at least one provider.");

        for (var i = 0; i < config.Providers.Count; i++)
        {
            var p = config.Providers[i];
            var label = string.IsNullOrWhiteSpace(p.CollectionName)
                ? $"providers[{i}]"
                : $"providers[{i}] ({p.CollectionName})";

            if (string.IsNullOrWhiteSpace(p.RepoPath))
                errors.Add($"{label}: repoPath is required.");

            if (string.IsNullOrWhiteSpace(p.CollectionName))
                errors.Add($"{label}: collectionName is required.");

            var specCount = new[] { p.OpenapiUrl, p.OpenapiPath, p.AssemblyPath }
                .Count(s => !string.IsNullOrWhiteSpace(s));

            if (specCount == 0)
                errors.Add($"{label}: no spec source set — add openapiUrl, openapiPath, or assemblyPath.");

            if (specCount > 1)
                errors.Add($"{label}: only one spec source can be set at a time.");
        }

        if (errors.Count > 0)
            throw new ConfigException(string.Join("\n", errors));
    }
}
