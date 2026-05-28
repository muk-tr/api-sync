using System.Diagnostics;

namespace api_sync.Git;

public static class GitRunner
{
    public static (bool ok, string output) Run(string workDir, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return (p.ExitCode == 0, output);
    }

    public static string? CurrentBranch(string repoDir)
    {
        var (ok, output) = Run(repoDir, "rev-parse --abbrev-ref HEAD");
        return ok ? output : null;
    }

    public static bool BranchExists(string repoDir, string branch)
    {
        var (ok, output) = Run(repoDir, $"branch --list {branch}");
        return ok && output.Length > 0;
    }

    public static bool HasCommits(string repoDir)
    {
        var (ok, _) = Run(repoDir, "rev-parse HEAD");
        return ok;
    }
}
