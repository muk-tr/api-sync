namespace api_sync.Models;

public record NormalizedRequest(
    string Method,
    string Path,
    string Name,
    List<string> Tags,
    bool HasBody
);
