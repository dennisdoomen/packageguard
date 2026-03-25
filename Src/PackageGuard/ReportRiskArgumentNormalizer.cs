namespace PackageGuard;

internal static class ReportRiskArgumentNormalizer
{
    private const string ReportRiskOption = "--report-risk";
    private const string ReportRiskAlias = "--reportrisk";

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

    private static bool IsReportRiskOption(string arg) =>
        arg.Equals(ReportRiskOption, StringComparison.OrdinalIgnoreCase) ||
        arg.Equals(ReportRiskAlias, StringComparison.OrdinalIgnoreCase);

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

    private static string GetOptionName(string arg) =>
        arg.StartsWith(ReportRiskAlias, StringComparison.OrdinalIgnoreCase) ? ReportRiskAlias : ReportRiskOption;

    private static bool LooksLikeOption(string arg) =>
        arg.StartsWith("-", StringComparison.Ordinal);
}
