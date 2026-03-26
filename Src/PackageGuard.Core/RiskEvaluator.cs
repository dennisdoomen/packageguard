using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

/// <summary>
/// Orchestrates the evaluation of all risk dimensions for a package and assembles the final <see cref="RiskDimensions"/>.
/// </summary>
public class RiskEvaluator
{
    private readonly ILogger logger;
    private readonly IEvaluateRiskDimension legalEvaluator;
    private readonly IEvaluateRiskDimension securityEvaluator;
    private readonly IEvaluateRiskDimension operationalEvaluator;

    /// <summary>
    /// Initializes a new instance of <see cref="RiskEvaluator"/> with the default dimension evaluators.
    /// </summary>
    public RiskEvaluator(ILogger logger)
        : this(logger, new LegalRiskEvaluator(), new SecurityRiskEvaluator(), new OperationalRiskEvaluator())
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="RiskEvaluator"/> with explicit dimension evaluators.
    /// </summary>
    internal RiskEvaluator(ILogger logger, IEvaluateRiskDimension legalEvaluator,
        IEvaluateRiskDimension securityEvaluator, IEvaluateRiskDimension operationalEvaluator)
    {
        this.logger = logger;
        this.legalEvaluator = legalEvaluator;
        this.securityEvaluator = securityEvaluator;
        this.operationalEvaluator = operationalEvaluator;
    }

    /// <summary>
    /// Evaluates the risk for a given package and updates its risk properties.
    /// </summary>
    public void EvaluateRisk(PackageInfo package)
    {
        logger.LogDebug("Evaluating risk for package {PackageName} {Version}", package.Name, package.Version);

        RiskDimensionEvaluation legalRisk = legalEvaluator.Evaluate(package);
        RiskDimensionEvaluation securityRisk = securityEvaluator.Evaluate(package);
        RiskDimensionEvaluation operationalRisk = operationalEvaluator.Evaluate(package);

        var riskDimensions = new RiskDimensions
        {
            LegalRisk = legalRisk.Score,
            LegalRiskRationale = legalRisk.Rationale,
            SecurityRisk = securityRisk.Score,
            SecurityRiskRationale = securityRisk.Rationale,
            OperationalRisk = operationalRisk.Score,
            OperationalRiskRationale = operationalRisk.Rationale
        };

        package.RiskDimensions = riskDimensions;
        package.RiskScore = riskDimensions.OverallRisk * 10; // Scale to 0-100

        logger.LogDebug(
            "Risk evaluation complete for {PackageName}: Overall={OverallRisk}, Legal={LegalRisk}, Security={SecurityRisk}, Operational={OperationalRisk}",
            package.Name,
            RiskEvaluationHelpers.FormatScore(package.RiskScore),
            RiskEvaluationHelpers.FormatScore(riskDimensions.LegalRisk),
            RiskEvaluationHelpers.FormatScore(riskDimensions.SecurityRisk),
            RiskEvaluationHelpers.FormatScore(riskDimensions.OperationalRisk));
    }
}
