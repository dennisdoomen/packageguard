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
}
