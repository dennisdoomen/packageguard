namespace PackageGuard;

/// <summary>
/// Normalizes command-line arguments so that an optional path value following <c>--report-risk</c>
/// is extracted and stored separately, keeping the remaining argument list well-formed for the Spectre.Console CLI parser.
/// </summary>
internal static class ReportRiskArgumentNormalizer
{
    /// <summary>
    /// The canonical <c>--report-risk</c> option name.
    /// </summary>
    private const string ReportRiskOption = "--report-risk";

    /// <summary>
    /// The alias <c>--reportrisk</c> option name accepted as an alternative to <see cref="ReportRiskOption"/>.
    /// </summary>
    private const string ReportRiskAlias = "--reportrisk";

    /// <summary>
    /// Normalizes <paramref name="args"/> by extracting any path argument that follows <c>--report-risk</c>
    /// (either inline as <c>--report-risk=path</c> or as a separate positional value) and returns the cleaned
    /// argument array alongside the extracted path.
    /// </summary>
    internal static (string[] Args, string? ReportRiskPath) Normalize(string[] args)
    {
        List<string> normalizedArgs = [];
        string? reportRiskPath = null;

        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];

            if (TryGetInlinePath(arg, out string? inlinePath))
            {
                normalizedArgs.Add(GetOptionName(arg));
                reportRiskPath = inlinePath;
                continue;
            }

            if (IsReportRiskOption(arg))
            {
                normalizedArgs.Add(arg);

                if (index + 1 < args.Length && !LooksLikeOption(args[index + 1]))
                {
                    reportRiskPath = args[index + 1];
                    index++;
                }

                continue;
            }

            normalizedArgs.Add(arg);
        }

        return (normalizedArgs.ToArray(), reportRiskPath);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="arg"/> is the <c>--report-risk</c> or <c>--reportrisk</c> option.
    /// </summary>
    private static bool IsReportRiskOption(string arg) =>
        arg.Equals(ReportRiskOption, StringComparison.OrdinalIgnoreCase) ||
        arg.Equals(ReportRiskAlias, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <c>true</c> and sets <paramref name="path"/> when <paramref name="arg"/> contains an inline
    /// path in the form <c>--report-risk=&lt;path&gt;</c> or <c>--reportrisk=&lt;path&gt;</c>.
    /// </summary>
    private static bool TryGetInlinePath(string arg, out string? path)
    {
        if (arg.StartsWith($"{ReportRiskOption}=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith($"{ReportRiskAlias}=", StringComparison.OrdinalIgnoreCase))
        {
            path = arg[(arg.IndexOf('=') + 1)..];
            return true;
        }

        path = null;
        return false;
    }

    /// <summary>
    /// Returns the canonical option name (<c>--report-risk</c> or <c>--reportrisk</c>) for the given argument,
    /// stripping any inline value.
    /// </summary>
    private static string GetOptionName(string arg) =>
        arg.StartsWith(ReportRiskAlias, StringComparison.OrdinalIgnoreCase) ? ReportRiskAlias : ReportRiskOption;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="arg"/> begins with a dash, indicating it is an option rather than a positional value.
    /// </summary>
    private static bool LooksLikeOption(string arg) =>
        arg.StartsWith("-", StringComparison.Ordinal);
}
