// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PackageGuard;
using Serilog;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;
using Vertical.SpectreLogger;
using ILogger = Microsoft.Extensions.Logging.ILogger;

var services = new ServiceCollection();

services.AddLogging(configure => configure
    .SetMinimumLevel(LogLevel.Debug)
    .AddSerilog()
    .AddSpectreConsole(b => b
        .ConfigureProfile( LogLevel.Trace,p => p.OutputTemplate = "[grey35]{Message}{NewLine}{Exception}[/]")
        .ConfigureProfile( LogLevel.Debug,p => p.OutputTemplate = "[grey46]{Message}{NewLine}{Exception}[/]")
        .ConfigureProfile( LogLevel.Information,p => p.OutputTemplate = "[grey85]{Message}{NewLine}{Exception}[/]")
        .ConfigureProfile( LogLevel.Warning,p => p.OutputTemplate = "[gold1]{Message}{NewLine}{Exception}[/]")
        .ConfigureProfile( LogLevel.Error,p => p.OutputTemplate = "[white on red1]{Message}{NewLine}{Exception}[/]")
        .SetMinimumLevel(LogLevel.Debug))
);

services.AddSingleton<ILogger>(sp => sp
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("PackageGuard"));

using var registrar = new DependencyInjectionRegistrar(services);

var app = new CommandApp<AnalyzeCommand>(registrar);
return app.Run(args);
