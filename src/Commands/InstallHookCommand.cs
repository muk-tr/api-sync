using api_sync.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace api_sync.Commands;

public class InstallHookCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
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
        var hookInstalled = InstallPostCheckoutHook(gitDir);

        AnsiConsole.MarkupLine(hookInstalled
            ? $"[green]✓[/] post-checkout hook → [bold]{hookPath}[/]"
            : $"[grey]  post-checkout hook already present[/]");

        var excludeInstalled = InstallGitExclude(gitDir);

        AnsiConsole.MarkupLine(excludeInstalled
            ? $"[green]✓[/] .api-sync.json added to [bold]{Path.Combine(gitDir, "info", "exclude")}[/]"
            : $"[grey]  .git/info/exclude already up to date[/]");

        AnsiConsole.MarkupLine("\n[grey]api-sync sync will run automatically on every branch switch.[/]");

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

    private static bool InstallPostCheckoutHook(string gitDir)
    {
        var hooksDir = Path.Combine(gitDir, "hooks");
        Directory.CreateDirectory(hooksDir);
        var hookPath = Path.Combine(hooksDir, "post-checkout");

        const string marker = "# >>> api-sync >>>";

        // Resolve how to invoke api-sync from a shell.
        // Prefer the global tool name; fall back to the DLL path for dev scenarios.
        var dllPath = typeof(InstallHookCommand).Assembly.Location;
        var bashDllPath = ToGitBashPath(dllPath);

        var block = $"""

            # >>> api-sync >>>
            # Syncs Bruno collection on branch switch (not file checkouts)
            if [ "$3" = "1" ]; then
              export PATH="$PATH:$HOME/.dotnet/tools"
              if command -v api-sync >/dev/null 2>&1; then
                api-sync sync
              else
                dotnet "{bashDllPath}" sync
              fi
            fi
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

        if (!OperatingSystem.IsWindows())
        {
            var fi = new FileInfo(hookPath);
            fi.UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        }

        return true;
    }

    // Converts a Windows path to the forward-slash format Git for Windows bash expects.
    // e.g. C:\Users\foo\bar.dll -> /c/Users/foo/bar.dll
    private static string ToGitBashPath(string path)
    {
        var p = path.Replace('\\', '/');
        if (p.Length >= 2 && p[1] == ':')
            p = "/" + char.ToLower(p[0]) + p[2..];
        return p;
    }
}
