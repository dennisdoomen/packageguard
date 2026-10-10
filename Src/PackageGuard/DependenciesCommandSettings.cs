using System.ComponentModel;
using JetBrains.Annotations;
using PackageGuard.Core.CSharp;
using Spectre.Console.Cli;

namespace PackageGuard;

/// <summary>
/// The findings that make the <c>dependencies</c> command exit with a non-zero code.
/// </summary>
public enum DependencySeverity
{
    /// <summary>
    /// Report findings, but always exit with code 0.
    /// </summary>
    Warning,

    /// <summary>
    /// Exit with code 1 when a reported finding exists.
    /// </summary>
    Error
}

/// <summary>
/// Defines the command-line settings for the <c>dependencies</c> command. It only reads the restore output of NuGet
/// projects, so it has none of the policy, risk, caching or npm options of the <c>analyze</c> command.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class DependenciesCommandSettings : CommandSettings
{
    [Description(
        "The path to a directory containing a .sln/.slnx file, a specific .sln/.slnx file, or a specific .csproj file. Defaults to the current working directory")]
    [CommandOption("-p|--path")]
    public string ProjectPath { get; set; } = string.Empty;

    [Description("Also report packages that resolve to different versions across projects.")]
    [CommandOption("--conflicts")]
    [DefaultValue(false)]
    public bool IncludeConflicts { get; set; }

    [Description(
        "Use \"error\" to exit with code 1 when a reported finding exists. Conflicts only count when --conflicts is passed. Defaults to \"warning\", which always exits with code 0.")]
    [CommandOption("--severity")]
    [DefaultValue(DependencySeverity.Warning)]
    public DependencySeverity Severity { get; set; } = DependencySeverity.Warning;

    [Description("A package id to leave out of the results. Can be repeated.")]
    [CommandOption("--exclude")]
    public string[] ExcludedPackageIds { get; set; } = [];

    [Description("Allow enabling or disabling an interactive mode of \"dotnet restore\". Defaults to true")]
    [CommandOption("-i|--restore-interactive|--restoreinteractive")]
    [DefaultValue(true)]
    public bool Interactive { get; set; } = true;

    [Description("Force restoring the NuGet dependencies, even if the lockfile is up-to-date")]
    [CommandOption("-f|--force-restore|--forcerestore")]
    [DefaultValue(false)]
    public bool ForceRestore { get; set; }

    [Description("Prevent the restore operation from running, even if the lock file is missing or out-of-date")]
    [CommandOption("-s|--skip-restore|--skiprestore")]
    [DefaultValue(false)]
    public bool SkipRestore { get; set; }

    [Description("Enable verbose (debug-level) logging output.")]
    [CommandOption("-v|--verbose")]
    [DefaultValue(false)]
    public bool Verbose { get; set; }

    /// <summary>
    /// Returns whether <paramref name="finding" /> is part of the output, and so counts towards the exit code.
    /// </summary>
    public bool IsReported(DependencyFinding finding) =>
        IncludeConflicts || finding.Kind != DependencyFindingKind.VersionConflict;
}
