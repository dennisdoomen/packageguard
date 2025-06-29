using Microsoft.Extensions.Logging;

namespace PackageGuard.Core.Common;

internal static class LoggerExtensions
{
    public static void LogHeader(this ILogger logger, string message, params object?[] args)
    {
        logger.LogInformation("");
        logger.LogInformation(
            "\u256c\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");

        logger.LogInformation("\u2551 " + message, args);
        logger.LogInformation("\u256c\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        logger.LogInformation("");
    }
}
