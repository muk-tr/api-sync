using api_sync.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace api_sync.Commands;

public class InstallHookCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        // Config must exist but we only need it to confirm we're in the right directory
        try
        {
            ConfigLoader.Load(Directory.GetCurrentDirectory());
        }
        catch (ConfigException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }

        var gitDir = FindGitDir(Directory.GetCurrentDirectory());
        if (gitDir is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No .git directory found. Run this from inside a git repository.");
            return 1;
        }

        var hookPath = Path.Combine(gitDir, "hooks", "post-checkout");
        var installed = InstallPostCheckoutHook(gitDir);

        AnsiConsole.MarkupLine(installed
            ? $"[green]✓[/] post-checkout hook installed → [bold]{hookPath}[/]"
            : $"[grey]  post-checkout hook already present — nothing changed.[/]");

        if (installed)
            AnsiConsole.MarkupLine("[grey]  api-sync sync will run automatically on every branch switch.[/]");

        return 0;
    }

    private static string? FindGitDir(string start)
    {
        var dir = start;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, ".git");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static bool InstallPostCheckoutHook(string gitDir)
    {
        var hooksDir = Path.Combine(gitDir, "hooks");
        Directory.CreateDirectory(hooksDir);
        var hookPath = Path.Combine(hooksDir, "post-checkout");

        const string marker = "# >>> api-sync >>>";
        const string block = """

            # >>> api-sync >>>
            # Syncs Bruno collection on branch switch (not file checkouts)
            [ "$3" = "1" ] && api-sync sync
            # <<< api-sync <<<
            """;

        if (File.Exists(hookPath))
        {
            if (File.ReadAllText(hookPath).Contains(marker)) return false;
            File.AppendAllText(hookPath, block);
        }
        else
        {
            File.WriteAllText(hookPath, "#!/bin/sh" + block);
        }

        // Make executable on non-Windows
        if (!OperatingSystem.IsWindows())
        {
            var fi = new FileInfo(hookPath);
            fi.UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        }

        return true;
    }
}
