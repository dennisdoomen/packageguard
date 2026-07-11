// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PackageGuard;
using Serilog;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;
using ILogger = Microsoft.Extensions.Logging.ILogger;

bool verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase)
            || args.Contains("-v", StringComparer.OrdinalIgnoreCase);
LogLevel minLogLevel = verbose ? LogLevel.Debug : LogLevel.Information;
Serilog.Events.LogEventLevel serilogLevel = minLogLevel == LogLevel.Debug
    ? Serilog.Events.LogEventLevel.Debug
    : Serilog.Events.LogEventLevel.Information;

var services = new ServiceCollection();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(serilogLevel)
    .WriteTo.Console()
    .CreateLogger();

services.AddLogging(configure => configure
    .SetMinimumLevel(minLogLevel)
    .AddSerilog());

services.AddSingleton<ILogger>(sp => sp
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("PackageGuard"));
using ServiceProvider serviceProvider = services.BuildServiceProvider();
ILogger logger = serviceProvider.GetRequiredService<ILogger>();

using var registrar = new DependencyInjectionRegistrar(services);

var app = new CommandApp<AnalyzeCommand>(registrar).WithData(logger);
app.Configure(c =>
{
    c.CaseSensitivity(CaseSensitivity.None);
});

string? previousReportRiskPath = Environment.GetEnvironmentVariable(AnalyzeCommandSettings.ReportRiskPathOverrideEnvironmentVariable);
(string[] normalizedArgs, string? reportRiskPath) = ReportRiskArgumentNormalizer.Normalize(args);
Environment.SetEnvironmentVariable(AnalyzeCommandSettings.ReportRiskPathOverrideEnvironmentVariable, reportRiskPath);

try
{
    return app.Run(normalizedArgs);
}
finally
{
    Environment.SetEnvironmentVariable(AnalyzeCommandSettings.ReportRiskPathOverrideEnvironmentVariable, previousReportRiskPath);
}
