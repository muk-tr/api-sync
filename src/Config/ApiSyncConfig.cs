namespace api_sync.Config;

public record ApiSyncConfig(
    List<string> SyncBranches,
    List<BrunoProviderConfig> Providers,
    string? ToolCommand = null  // how to invoke api-sync — defaults to "api-sync" (global install)
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
