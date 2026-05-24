using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using api_sync.Config;
using api_sync.Utilities;
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

        // --- Step 1: detect web projects and pick which ones to include ---
        var currentDir = Directory.GetCurrentDirectory();
        var detectedProjects = ProjectDetector.FindProjects(currentDir);

        List<DetectedProject> selectedProjects;
        List<string> manualNames = [];

        if (detectedProjects.Count == 0)
        {
            var name = AnsiConsole.Prompt(
                new TextPrompt<string>("Collection name:")
                    .DefaultValue("my-api")
            );
            selectedProjects = [];
            manualNames = [name];
        }
        else
        {
            var chosen = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Which projects should have a Bruno collection? [grey](Space to select, Enter to confirm)[/]")
                    .AddChoices(detectedProjects.Select(p => p.Name))
            );
            selectedProjects = detectedProjects.Where(p => chosen.Contains(p.Name)).ToList();
        }

        // --- Step 2: shared Bruno settings ---
        AnsiConsole.MarkupLine("\n[bold]Bruno collections[/]");

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        var baseFolder = AnsiConsole.Prompt(
            new TextPrompt<string>("Base folder for Bruno collections:")
                .DefaultValue(desktopPath)
        );

        var groupBy = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Group requests by:")
                .AddChoices("tags", "path")
        );

        // --- Step 3: per-project spec source ---
        var providers = new List<BrunoProviderConfig>();

        var projectEntries = selectedProjects.Count > 0
            ? selectedProjects.Select(p => (Label: p.Name, CollectionName: p.CollectionName, Project: (DetectedProject?)p))
            : manualNames.Select(n => (Label: n, CollectionName: n, Project: (DetectedProject?)null));

        foreach (var entry in projectEntries)
        {
            AnsiConsole.MarkupLine($"\n[bold]{entry.Label}[/]");

            var specSource = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("  Spec source:")
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
                        new TextPrompt<string>("  Path to [green]swagger.json[/] or [green]openapi.yaml[/]:")
                            .DefaultValue("./docs/swagger.json")
                    );
                    break;

                case "URL (running endpoint)":
                    openapiUrl = AnsiConsole.Prompt(
                        new TextPrompt<string>("  OpenAPI URL:")
                            .DefaultValue("https://localhost:5001/swagger/v1/swagger.json")
                    );
                    break;

                case ".NET assembly (fallback)":
                    var assemblyDefault = entry.Project?.DefaultAssemblyPath
                        ?? "./bin/Debug/net10.0/MyApi.dll";
                    assemblyPath = AnsiConsole.Prompt(
                        new TextPrompt<string>("  Path to [green].dll[/]:")
                            .DefaultValue(assemblyDefault)
                    );
                    break;
            }

            providers.Add(new BrunoProviderConfig(
                Type: "bruno",
                RepoPath: Path.Combine(baseFolder, entry.CollectionName),
                CollectionName: entry.CollectionName,
                GroupBy: groupBy,
                OpenapiUrl: openapiUrl,
                OpenapiPath: openapiPath,
                AssemblyPath: assemblyPath
            ));
        }

        // --- Sync branches ---
        var branchesInput = AnsiConsole.Prompt(
            new TextPrompt<string>("\nSync branches [grey](comma-separated, supports prefix/* wildcards)[/]:")
                .DefaultValue("main, develop, feature/*")
        );

        var syncBranches = branchesInput
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        // --- Build and write config ---
        var config = new ApiSyncConfig(
            SyncBranches: syncBranches,
            Providers: providers
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
