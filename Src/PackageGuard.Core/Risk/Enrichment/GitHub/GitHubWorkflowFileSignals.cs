namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Signals detected in workflow YAML files.</summary>
internal sealed class GitHubWorkflowFileSignals
{
    /// <summary>Indicates whether workflow files contain a provenance attestation signal (SLSA, attest, provenance).</summary>
    public bool HasProvenanceAttestation { get; init; }

    /// <summary>Indicates whether workflow files reference a code coverage tool or service.</summary>
    public bool HasCoverageSignal { get; init; }

    /// <summary>Indicates whether workflow files reference deterministic or reproducible build tooling.</summary>
    public bool HasReproducibleBuildSignal { get; init; }

    /// <summary>Indicates whether the repository uses automated dependency update tooling (Dependabot or Renovate).</summary>
    public bool HasDependencyUpdateAutomation { get; init; }

    /// <summary>Indicates whether workflow files or repository structure suggest automated test execution.</summary>
    public bool HasTestSignal { get; init; }

    /// <summary>The number of distinct operating system platforms targeted by workflow files.</summary>
    public int PlatformCount { get; init; }
}
