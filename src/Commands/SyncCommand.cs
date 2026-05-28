using api_sync.Bruno;
using api_sync.Config;
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

        var success = true;

        foreach (var provider in config.Providers)
        {
            AnsiConsole.MarkupLine($"\n[bold]{provider.CollectionName}[/]");

            try
            {
                var requests = await AnsiConsole.Status()
                    .StartAsync("Reading spec...", _ => OpenApiScanner.ScanAsync(provider));

                AnsiConsole.MarkupLine($"  [green]✓[/] {requests.Count} endpoints found");

                foreach (var r in requests)
                {
                    var tags = r.Tags.Count > 0 ? $"[grey]({string.Join(", ", r.Tags)})[/]" : "";
                    AnsiConsole.MarkupLine($"    [cyan]{r.Method,-7}[/] {r.Path} {tags}");
                }

                BrunoWriter.Write(provider, requests);
                AnsiConsole.MarkupLine($"  [green]✓[/] Written to [bold]{provider.RepoPath}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"  [red]✗[/] {ex.Message}");
                success = false;
            }
        }

        return success ? 0 : 1;
    }
}
