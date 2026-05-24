using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using api_sync.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace api_sync.Commands;

public class InitSettings : CommandSettings
{
    [CommandOption("--force|-f")]
    [Description("Overwrite existing .api-sync.json without prompting")]
    public bool Force { get; set; }
}

public class InitCommand : Command<InitSettings>
{
    private const string ConfigFileName = ".api-sync.json";

    protected override int Execute(CommandContext context, InitSettings settings, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);

        if (File.Exists(configPath) && !settings.Force)
        {
            var overwrite = AnsiConsole.Confirm(
                $"[yellow]{ConfigFileName}[/] already exists. Overwrite it?",
                defaultValue: false
            );

            if (!overwrite)
            {
                AnsiConsole.MarkupLine("[grey]Aborted.[/]");
                return 0;
            }
        }

        AnsiConsole.WriteLine();

        // --- Spec source ---
        var specSource = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("How will api-sync read your API spec?")
                .AddChoices(
                    "Local file path",
                    "URL (running endpoint)",
                    ".NET assembly (fallback)"
                )
        );

        string? openapiUrl = null;
        string? openapiPath = null;
        string? assemblyPath = null;

        switch (specSource)
        {
            case "Local file path":
                openapiPath = AnsiConsole.Prompt(
                    new TextPrompt<string>("Path to your [green]swagger.json[/] or [green]openapi.yaml[/]:")
                        .DefaultValue("./docs/swagger.json")
                );
                break;

            case "URL (running endpoint)":
                openapiUrl = AnsiConsole.Prompt(
                    new TextPrompt<string>("OpenAPI URL:")
                        .DefaultValue("https://localhost:5001/swagger/v1/swagger.json")
                );
                break;

            case ".NET assembly (fallback)":
                assemblyPath = AnsiConsole.Prompt(
                    new TextPrompt<string>("Path to your [green].dll[/]:")
                        .DefaultValue("./bin/Debug/net10.0/MyApi.dll")
                );
                break;
        }

        // --- Sync branches (commented out until sync command is implemented) ---
        // var branchesInput = AnsiConsole.Prompt(
        //     new TextPrompt<string>("Sync branches [grey](comma-separated, supports prefix/* wildcards)[/]:")
        //         .DefaultValue("main, develop, feature/*")
        // );
        // var syncBranches = branchesInput
        //     .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        //     .ToList();
        var syncBranches = new List<string>();

        // --- Bruno collection ---
        AnsiConsole.MarkupLine("\n[bold]Bruno collection[/]");

        var repoPath = AnsiConsole.Prompt(
            new TextPrompt<string>("Path to your Bruno collection repo:")
                .DefaultValue("../my-project-bruno")
        );

        var collectionName = AnsiConsole.Prompt(
            new TextPrompt<string>("Collection name:")
                .DefaultValue("my-api")
        );

        var groupBy = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Group requests by:")
                .AddChoices("tags", "path")
        );

        // --- Build and write config ---
        var config = new ApiSyncConfig(
            OpenapiUrl: openapiUrl,
            OpenapiPath: openapiPath,
            AssemblyPath: assemblyPath,
            SyncBranches: syncBranches,
            Providers: [new BrunoProviderConfig("bruno", repoPath, collectionName, groupBy)]
        );

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        File.WriteAllText(configPath, json);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✓[/] Created [bold]{ConfigFileName}[/]");
        AnsiConsole.MarkupLine("[grey]  Edit it to point at your spec and Bruno repo, then run [bold]api-sync sync[/].[/]");

        return 0;
    }
}
