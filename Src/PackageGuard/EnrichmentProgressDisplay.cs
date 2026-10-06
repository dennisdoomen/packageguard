using Spectre.Console;

namespace PackageGuard;

/// <summary>
/// Shows an animated progress bar while packages are being enriched with risk data. The bar starts on the first
/// report and ends, leaving its final state on screen, when the last package has been reported.
/// </summary>
internal sealed class EnrichmentProgressDisplay
{
    private readonly Lock syncRoot = new();
    private Task? display;
    private ProgressTask? task;

    /// <summary>
    /// An animated bar needs an interactive terminal, and in verbose mode the debug output would garble it,
    /// so the periodic log lines remain the progress indicator there.
    /// </summary>
    public static bool IsSupported(bool verbose) => !verbose && AnsiConsole.Profile.Capabilities.Interactive;

    /// <summary>
    /// Can be called from multiple threads.
    /// </summary>
    public void Report(int completed, int total)
    {
        Task? displayToAwait = null;

        lock (syncRoot)
        {
            task ??= Start(total);

            task.Value = Math.Max(task.Value, completed);
            task.Description = $"Enriched {task.Value:0}/{total} packages";

            if (completed == total)
            {
                task.StopTask();
                displayToAwait = display;
            }
        }

        // Wait for the final render so that the log lines that follow do not interleave with the bar
        displayToAwait?.Wait();
    }

    private ProgressTask Start(int total)
    {
        var ready = new TaskCompletionSource<ProgressTask>();

        display = Task.Run(() => AnsiConsole.Progress()
            .Columns(
                new SpinnerColumn(),
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn())
            .StartAsync(async context =>
            {
                ProgressTask progressTask = context.AddTask($"Enriched 0/{total} packages", maxValue: total);
                ready.SetResult(progressTask);

                while (!context.IsFinished)
                {
                    await Task.Delay(100);
                }
            }));

        return ready.Task.GetAwaiter().GetResult();
    }
}
