namespace api_sync.Git;

public static class BrunoRepo
{
    /// <summary>
    /// Ensures the Bruno collection folder is a git repo and has the right branch checked out.
    /// Call this BEFORE writing files.
    /// </summary>
    public static void CheckoutBranch(string repoPath, string branch)
    {
        Directory.CreateDirectory(repoPath);

        var isRepo = Directory.Exists(Path.Combine(repoPath, ".git"));
        if (!isRepo)
        {
            GitRunner.Run(repoPath, "init");
            // Branch will be renamed to `branch` after the initial commit in CommitChanges
            return;
        }

        if (GitRunner.BranchExists(repoPath, branch))
            GitRunner.Run(repoPath, $"checkout {branch}");
        else
            GitRunner.Run(repoPath, $"checkout -b {branch}");
    }

    /// <summary>
    /// Stages all changes and commits them. Returns true if a commit was made.
    /// Call this AFTER writing files.
    /// </summary>
    public static bool CommitChanges(string repoPath, string branch)
    {
        GitRunner.Run(repoPath, "add -A");

        var (_, status) = GitRunner.Run(repoPath, "status --porcelain");
        if (string.IsNullOrWhiteSpace(status))
            return false;

        if (!GitRunner.HasCommits(repoPath))
        {
            GitRunner.Run(repoPath, $"commit -m \"initial sync: {branch}\"");
            // Rename whatever default branch git init created to the target branch
            var (_, current) = GitRunner.Run(repoPath, "rev-parse --abbrev-ref HEAD");
            if (current != branch)
                GitRunner.Run(repoPath, $"branch -m {branch}");
        }
        else
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
            GitRunner.Run(repoPath, $"commit -m \"sync: {branch} @ {timestamp} UTC\"");
        }

        return true;
    }
}
