using api_sync.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("api-sync");

    config.AddCommand<InitCommand>("init")
        .WithDescription("Create a .api-sync.json config file with defaults");

    config.AddCommand<SyncCommand>("sync")
        .WithDescription("Sync the current branch to all configured Bruno collections");

    config.AddCommand<InstallHookCommand>("install-hook")
        .WithDescription("Install git hooks and MSBuild targets to auto-sync on branch switch or build");
});

return app.Run(args);
