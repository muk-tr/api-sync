using api_sync.Bruno;
using api_sync.Config;
using api_sync.Git;
using api_sync.Scanner;
using Spectre.Console;
using Spectre.Console.Cli;

namespace api_sync.Commands;

public class SyncCommand : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
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

        var currentBranch = GitRunner.CurrentBranch(Directory.GetCurrentDirectory());

        if (currentBranch is null)
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] Not in a git repository — branch tracking disabled.");
        }
        else if (!MatchesBranchPattern(currentBranch, config.SyncBranches))
        {
            AnsiConsole.MarkupLine($"[grey]Branch [bold]{currentBranch}[/] is not in syncBranches — nothing to sync.[/]");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[grey]Branch:[/] [bold]{currentBranch}[/]");
        }

        var success = true;

        foreach (var provider in config.Providers)
        {
            AnsiConsole.MarkupLine($"\n[bold]{provider.CollectionName}[/]");

            try
            {
                if (currentBranch is not null)
                    BrunoRepo.CheckoutBranch(provider.RepoPath, currentBranch);

                var requests = await AnsiConsole.Status()
                    .StartAsync("Reading spec...", _ => OpenApiScanner.ScanAsync(provider));

                AnsiConsole.MarkupLine($"  [green]✓[/] {requests.Count} endpoints found");

                foreach (var r in requests)
                {
                    var tags = r.Tags.Count > 0 ? $"[grey]({string.Join(", ", r.Tags)})[/]" : "";
                    AnsiConsole.MarkupLine($"    [cyan]{r.Method,-7}[/] {r.Path} {tags}");
                }

                BrunoWriter.Write(provider, requests, currentBranch);

                if (currentBranch is not null)
                {
                    var committed = BrunoRepo.CommitChanges(provider.RepoPath, currentBranch);
                    AnsiConsole.MarkupLine(committed
                        ? $"  [green]✓[/] Committed to [bold]{provider.RepoPath}[/]"
                        : $"  [grey]  No changes to commit[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"  [green]✓[/] Written to [bold]{provider.RepoPath}[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"  [red]✗[/] {ex.Message}");
                success = false;
            }
        }

        return success ? 0 : 1;
    }

    private static bool MatchesBranchPattern(string branch, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.Contains('*'))
            {
                var prefix = pattern[..pattern.IndexOf('*')];
                if (branch.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (branch.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
