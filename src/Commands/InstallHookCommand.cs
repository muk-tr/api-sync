using api_sync.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace api_sync.Commands;

public class InstallHookCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        ApiSyncConfig config;
        try
        {
            config = ConfigLoader.Load(Directory.GetCurrentDirectory());
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

        // toolCommand from config, or "api-sync" if installed globally
        var toolCommand = config.ToolCommand ?? "api-sync";

        var hookPath = Path.Combine(gitDir, "hooks", "post-checkout");
        var hookInstalled = InstallPostCheckoutHook(gitDir, toolCommand);

        AnsiConsole.MarkupLine(hookInstalled
            ? $"[green]✓[/] post-checkout hook → [bold]{hookPath}[/]"
            : $"[grey]  post-checkout hook updated[/]");

        var excludeInstalled = InstallGitExclude(gitDir);

        AnsiConsole.MarkupLine(excludeInstalled
            ? $"[green]✓[/] .api-sync.json added to [bold]{Path.Combine(gitDir, "info", "exclude")}[/]"
            : $"[grey]  .git/info/exclude already up to date[/]");

        AnsiConsole.MarkupLine($"\n[grey]Using command:[/] [bold]{toolCommand}[/]");
        AnsiConsole.MarkupLine("[grey]api-sync sync will run automatically on every branch switch.[/]");

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

    private static bool InstallGitExclude(string gitDir)
    {
        var infoDir = Path.Combine(gitDir, "info");
        Directory.CreateDirectory(infoDir);
        var excludePath = Path.Combine(infoDir, "exclude");

        const string marker = "# api-sync";
        const string entry = ".api-sync.json";

        if (File.Exists(excludePath))
        {
            var existing = File.ReadAllText(excludePath);
            if (existing.Contains(marker)) return false;
            File.AppendAllText(excludePath, $"\n{marker}\n{entry}\n");
        }
        else
        {
            File.WriteAllText(excludePath, $"{marker}\n{entry}\n");
        }

        return true;
    }

    private static bool InstallPostCheckoutHook(string gitDir, string toolCommand)
    {
        var hooksDir = Path.Combine(gitDir, "hooks");
        Directory.CreateDirectory(hooksDir);
        var hookPath = Path.Combine(hooksDir, "post-checkout");

        const string marker = "# >>> api-sync >>>";
        const string endMarker = "# <<< api-sync <<<";

        var block = $"""

            # >>> api-sync >>>
            # Syncs Bruno collection on branch switch (not file checkouts)
            if [ "$3" = "1" ]; then
              export PATH="$PATH:$HOME/.dotnet/tools"
              {toolCommand} sync
            fi
            # <<< api-sync <<<
            """;

        if (File.Exists(hookPath))
        {
            var existing = File.ReadAllText(hookPath);

            if (existing.Contains(marker))
            {
                // Replace the existing api-sync block
                var start = existing.IndexOf(marker, StringComparison.Ordinal);
                var end = existing.IndexOf(endMarker, start, StringComparison.Ordinal);
                if (end >= 0)
                {
                    var after = existing[(end + endMarker.Length)..];
                    var updated = existing[..start].TrimEnd() + block + after;
                    if (updated == existing) return false; // nothing changed
                    File.WriteAllText(hookPath, updated);
                    return true;
                }
            }

            File.AppendAllText(hookPath, block);
        }
        else
        {
            File.WriteAllText(hookPath, "#!/bin/sh" + block);
        }

        if (!OperatingSystem.IsWindows())
        {
            var fi = new FileInfo(hookPath);
            fi.UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        }

        return true;
    }
}
