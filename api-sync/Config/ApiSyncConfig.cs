namespace api_sync.Config;

public record ApiSyncConfig(
    List<string> SyncBranches,
    List<BrunoProviderConfig> Providers
);

public record BrunoProviderConfig(
    string Type,
    string RepoPath,
    string CollectionName,
    string GroupBy,
    string? OpenapiUrl,
    string? OpenapiPath,
    string? AssemblyPath
);
