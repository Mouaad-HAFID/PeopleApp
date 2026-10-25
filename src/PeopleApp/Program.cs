using PeopleApp.Api;
using PeopleApp.ConsoleUi;
using PeopleApp.Logging;
using Spectre.Console;

const int CancelledExitCode = 130;

using var cancellation = new CancellationTokenSource();
// First Ctrl+C requests a graceful stop; a second one falls through to the default termination.
Console.CancelKeyPress += (_, e) =>
{
    if (!cancellation.IsCancellationRequested)
    {
        e.Cancel = true;
        cancellation.Cancel();
    }
};

var console = AnsiConsole.Console;
using var log = CreateLog(console);
using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

try
{
    PeopleServiceSession? session = null;
    while (session is null)
    {
        var connected = await ConsoleReadOperation.TryRunAsync(
            console,
            "Opening service session...",
            async () => session = await PeopleServiceSession.OpenDefaultAsync(httpClient, cancellation.Token, log),
            cancellation.Token);
        if (!connected && !await new ConfirmationPrompt("Try opening a session again?") { DefaultValue = false }
                .ShowAsync(console, cancellation.Token))
        {
            return 1;
        }
    }

    await new PeopleConsoleApplication(session, console).RunAsync(cancellation.Token);
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    console.WriteLine();
    return CancelledExitCode;
}

static JsonLinesOperationLog? CreateLog(IAnsiConsole console)
{
    var directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeopleApp", "logs");
    try
    {
        var log = JsonLinesOperationLog.Create(directory);
        console.MarkupLineInterpolated($"[grey]Operation log: {log.FilePath}[/]");
        return log;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        console.MarkupLineInterpolated($"[yellow]Operation logging is disabled: {exception.Message}[/]");
        return null;
    }
}
