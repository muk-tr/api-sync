namespace api_sync.Config;

public record ApiSyncConfig(
    string? OpenapiUrl,
    string? OpenapiPath,
    string? AssemblyPath,
    List<string> SyncBranches,
    List<BrunoProviderConfig> Providers
);

public record BrunoProviderConfig(
    string Type,
    string RepoPath,
    string CollectionName,
    string GroupBy
);
