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

var app = new CommandApp(registrar);
app.SetDefaultCommand<AnalyzeCommand>();
app.Configure(c =>
{
    c.CaseSensitivity(CaseSensitivity.None);

    // Spectre.Console.Cli only prints the exception's message by default, discarding the stack trace.
    // Always log the full exception so unexpected failures can be diagnosed from the console output.
    c.SetExceptionHandler((ex, _) =>
    {
        logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
        return -1;
    });

    c.AddCommand<AnalyzeCommand>("analyze")
        .WithDescription("Analyzes NuGet/NPM dependencies against the configured allow/deny policies.");
    c.AddCommand<ExplainCommand>("explain")
        .WithDescription("Explains why a specific package is present and how it was evaluated against policy.");
    c.AddCommand<InitCommand>("init")
        .WithDescription("Scans the repository and scaffolds a configuration file from the licenses actually found.");
});

return app.Run(args);
