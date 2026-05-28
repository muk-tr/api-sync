using System.Xml.Linq;
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

        AnsiConsole.MarkupLine("[bold]Installing hooks[/]\n");

        var success = true;

        // Always install the post-checkout hook
        var hookInstalled = InstallPostCheckoutHook(gitDir);
        AnsiConsole.MarkupLine(hookInstalled
            ? $"  [green]✓[/] post-checkout hook → [bold]{Path.Combine(gitDir, "hooks", "post-checkout")}[/]"
            : $"  [grey]  post-checkout hook already installed[/]");

        // For assembly-path providers, also add a MSBuild target
        foreach (var provider in config.Providers.Where(p => p.AssemblyPath is not null))
        {
            AnsiConsole.MarkupLine($"\n  [bold]{provider.CollectionName}[/] uses assembly scanning");

            var csprojPath = FindCsproj(provider.AssemblyPath!, Directory.GetCurrentDirectory());
            if (csprojPath is null)
            {
                AnsiConsole.MarkupLine($"  [yellow]⚠[/] Could not find .csproj for {provider.AssemblyPath}");
                AnsiConsole.MarkupLine($"    Add this target manually to your project file:");
                AnsiConsole.MarkupLine($"    [grey]{BuildMsbuildTargetSnippet(Directory.GetCurrentDirectory())}[/]");
                success = false;
                continue;
            }

            var targetInstalled = InstallMsbuildTarget(csprojPath, Directory.GetCurrentDirectory());
            AnsiConsole.MarkupLine(targetInstalled
                ? $"  [green]✓[/] AfterTargets=\"Build\" → [bold]{csprojPath}[/]"
                : $"  [grey]  MSBuild target already installed in {csprojPath}[/]");
        }

        AnsiConsole.MarkupLine(success
            ? "\n[green]Done.[/] api-sync sync will run automatically on branch switch."
            : "\n[yellow]Done with warnings.[/] Fix the items above and re-run.");

        return success ? 0 : 1;
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
            # Syncs Bruno collection to the checked-out branch (branch checkouts only)
            [ "$3" = "1" ] && api-sync sync
            # <<< api-sync <<<
            """;

        if (File.Exists(hookPath))
        {
            var existing = File.ReadAllText(hookPath);
            if (existing.Contains(marker)) return false; // already installed
            File.AppendAllText(hookPath, block);
        }
        else
        {
            File.WriteAllText(hookPath, "#!/bin/sh" + block);
        }

        // Make executable on non-Windows (no-op on Windows)
        if (!OperatingSystem.IsWindows())
        {
            var fi = new FileInfo(hookPath);
            fi.UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        }

        return true;
    }

    private static string? FindCsproj(string assemblyPath, string configDir)
    {
        var fullAssemblyPath = Path.IsPathRooted(assemblyPath)
            ? assemblyPath
            : Path.GetFullPath(Path.Combine(configDir, assemblyPath));

        var dir = Path.GetDirectoryName(fullAssemblyPath);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var found = Directory.GetFiles(dir, "*.csproj").FirstOrDefault();
            if (found is not null) return found;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static bool InstallMsbuildTarget(string csprojPath, string configDir)
    {
        var doc = XDocument.Load(csprojPath);
        var ns = doc.Root!.Name.Namespace;

        if (doc.Descendants(ns + "Target").Any(t => t.Attribute("Label")?.Value == "api-sync"))
            return false; // already installed

        var workDir = Path.GetRelativePath(Path.GetDirectoryName(csprojPath)!, configDir)
            .Replace('\\', '/');

        var target = new XElement(ns + "Target",
            new XAttribute("Name", "ApiSync"),
            new XAttribute("AfterTargets", "Build"),
            new XAttribute("Label", "api-sync"),
            new XElement(ns + "Exec",
                new XAttribute("Command", "api-sync sync"),
                new XAttribute("WorkingDirectory", $"$(MSBuildProjectDirectory)/{workDir}")
            )
        );

        doc.Root.Add(target);
        doc.Save(csprojPath);
        return true;
    }

    private static string BuildMsbuildTargetSnippet(string configDir) =>
        """
        <Target Name="ApiSync" AfterTargets="Build" Label="api-sync">
          <Exec Command="api-sync sync" WorkingDirectory="..." />
        </Target>
        """;
}
