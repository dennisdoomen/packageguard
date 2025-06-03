using System.ComponentModel;
using JetBrains.Annotations;
using Spectre.Console.Cli;

namespace PackageGuard;

[UsedImplicitly]
internal class AnalyzeCommandSettings : CommandSettings
{
    [Description(
        "The path to a directory containing a .sln file, a specific .sln file, or a specific .csproj file. Defaults to the current working directory")]
    [CommandArgument(0, "[path]")]
    public string ProjectPath { get; set; } = string.Empty;

    [Description("The path to the configuration file. Defaults to the config.json in the current working directory.")]
    [CommandOption("--configPath")]
    public string ConfigPath { get;  set; } = "config.json";

    [Description("Allow enabling or disabling an interactive mode of \"dotnet restore\". Defaults to true")]
    [CommandOption("--restore-interactive")]
    public bool Interactive {get; set;} = true;

    [Description("Force restoring the NuGet dependencies, even if the lockfile is up-to-date")]
    [CommandOption("--force-restore")]
    public bool ForceRestore { get; set; } = false;

    [Description("Prevent the restore operation from running, even if the lock file is missing or out-of-date")]
    [CommandOption("--skip-restore")]
    public bool SkipRestore { get; set; } = false;
}
