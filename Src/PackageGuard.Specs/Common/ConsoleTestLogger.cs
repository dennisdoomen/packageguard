using Microsoft.Extensions.Logging;

namespace PackageGuard.Specs.Common;

internal static class ConsoleTestLogger
{
    // Call ConsoleTestLogger.Create<ProjectAnalyzer>() or Create("Tests")
    public static ILogger Create<T>(LogLevel minimumLevel = LogLevel.Information) =>
        Create(typeof(T).FullName ?? "Tests", minimumLevel);

    public static ILogger Create(string category, LogLevel minimumLevel = LogLevel.Information)
    {
        var factory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(minimumLevel);
        });

        return factory.CreateLogger(category);
    }
}
