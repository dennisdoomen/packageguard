// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PackageGuard;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;
using ILogger = Microsoft.Extensions.Logging.ILogger;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(theme: AnsiConsoleTheme.Literate)
    .CreateLogger();

var services = new ServiceCollection();

services.AddLogging(configure => configure
    .SetMinimumLevel(LogLevel.Debug)
    .AddSerilog());

services.AddSingleton<ILogger>(sp => sp
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("PackageGuard"));

using var registrar = new DependencyInjectionRegistrar(services);

var app = new CommandApp<AnalyzeCommand>(registrar);
app.Configure(c =>
    c.CaseSensitivity(CaseSensitivity.None));

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
