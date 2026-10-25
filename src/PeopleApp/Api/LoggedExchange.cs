using System.Diagnostics;
using PeopleApp.Logging;

namespace PeopleApp.Api;

internal sealed class LoggedExchange(
    IOperationLog log,
    string operation,
    HttpMethod method,
    CancellationToken cancellationToken)
{
    private readonly long _started = Stopwatch.GetTimestamp();

    public string Stage { get; set; } = "send";

    public int? Status { get; set; }

    public void Completed(string outcome = "succeeded") => Write(outcome, null, null);

    public void Failed(Exception exception)
    {
        var outcome = exception is OperationCanceledException
            ? cancellationToken.IsCancellationRequested ? "cancelled" : "timed_out"
            : "failed";
        Write(outcome, Stage, exception.GetType().Name);
    }

    private void Write(string outcome, string? stage, string? errorType) =>
        log.Write(new OperationLogEntry(
            DateTimeOffset.UtcNow,
            operation,
            method.Method,
            Status,
            (long)Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
            outcome,
            stage,
            errorType));
}
