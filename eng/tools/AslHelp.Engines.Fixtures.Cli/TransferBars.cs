using Spectre.Console;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Shows a progress bar with the size, the speed and the time left for each download and upload.
/// </summary>
/// <remarks>
///     A bar starts with the 1st report that has a length, so a transfer that reports nothing or no length shows none.
///     The bar is drawn only while the transfer runs and is cleared when it ends, so nothing else may write to the
///     console until then. On an output that is not a terminal, a transfer prints its name and its percentage instead,
///     once when it starts, at 25, 50 and 75 percent and from 95 percent on.
/// </remarks>
internal sealed class TransferBars
{
    private readonly IAnsiConsole _console;

    private TransferBars(IAnsiConsole console)
    {
        _console = console;
    }

    /// <summary>
    ///     Creates the bars that draw on <paramref name="output"/>.
    /// </summary>
    /// <remarks>
    ///     The console of this process gets the bars that it supports. Any other writer gets plain lines with no ANSI codes,
    ///     because Spectre.Console takes the ANSI support from the console and the CI variables of the process, not from
    ///     the writer.
    /// </remarks>
    /// <param name="output">The writer of the steps, which the bars write to as well.</param>
    /// <returns>
    ///     The bars.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static TransferBars Create(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (ReferenceEquals(output, Console.Out))
        {
            return new TransferBars(AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output) }));
        }

        AnsiConsoleSettings settings = new()
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(output),
        };

        // Turn the enrichers off too, because the one for GitHub Actions turns ANSI on again after the settings apply.
        settings.Enrichment.UseDefaultEnrichers = false;

        return new TransferBars(AnsiConsole.Create(settings));
    }

    /// <summary>
    ///     Runs <paramref name="transfer"/> and shows its reports as a bar called <paramref name="description"/>.
    /// </summary>
    /// <param name="description">The name of the bar, such as the file or the store path.</param>
    /// <param name="transfer">The transfer, which reports to the receiver it gets.</param>
    /// <returns>
    ///     A task that completes once the transfer completes.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="description"/> is empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> or <paramref name="transfer"/> is <see langword="null"/>.</exception>
    public async Task ShowAsync(string description, Func<IProgress<TransferProgress>, Task> transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        _ = await ShowAsync(
            description,
            async progress =>
            {
                await transfer(progress).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs <paramref name="transfer"/> and shows its reports as a bar called <paramref name="description"/>.
    /// </summary>
    /// <typeparam name="T">The type of the result of the transfer.</typeparam>
    /// <param name="description">The name of the bar, such as the file or the store path.</param>
    /// <param name="transfer">The transfer, which reports to the receiver it gets.</param>
    /// <returns>
    ///     The result of the transfer.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="description"/> is empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> or <paramref name="transfer"/> is <see langword="null"/>.</exception>
    public Task<T> ShowAsync<T>(string description, Func<IProgress<TransferProgress>, Task<T>> transfer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(transfer);

        return _console.Progress()
            .AutoClear(true)
            .ExcludeVerticalPadding(true)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new DownloadedColumn(),
                new TransferSpeedColumn(),
                new RemainingTimeColumn())
            .StartAsync(context => transfer(new Bar(context, description)));
    }

    /// <summary>
    ///     Moves 1 bar to each report of a transfer and adds the bar on the 1st report that has a length.
    /// </summary>
    /// <param name="context">The progress display that the bar belongs to.</param>
    /// <param name="description">The name of the bar.</param>
    private sealed class Bar(
        ProgressContext context,
        string description) : IProgress<TransferProgress>
    {
        private ProgressTask? _task;

        /// <inheritdoc/>
        public void Report(TransferProgress value)
        {
            // Skip a report with no length, because a bar without an end would count as done at its 1st chunk.
            if (value.Length is not { } length)
            {
                return;
            }

            // Escape the name, because the display reads it as markup and a '[' in a path would throw on its refresh thread.
            _task ??= context.AddTask(Markup.Escape(description), maxValue: length);

            // Set the length only when it changes, because every change adds a sample to the speed.
            if (_task.MaxValue != length)
            {
                _task.MaxValue = length;
            }

            _task.Value = value.Bytes;
        }
    }
}
