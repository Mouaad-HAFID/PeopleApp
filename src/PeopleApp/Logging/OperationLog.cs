using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeopleApp.Logging;

/// <summary>
/// One structured entry per HTTP exchange. Contains no URLs, session keys, person data, or bodies.
/// </summary>
public sealed record OperationLogEntry(
    DateTimeOffset Timestamp,
    string Operation,
    string Method,
    int? Status,
    long DurationMs,
    string Outcome,
    string? FailureStage,
    string? ErrorType);

public interface IOperationLog
{
    void Write(OperationLogEntry entry);
}

public sealed class NullOperationLog : IOperationLog
{
    public static readonly NullOperationLog Instance = new();

    public void Write(OperationLogEntry entry)
    {
    }
}

public sealed class JsonLinesOperationLog : IOperationLog, IDisposable
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Lock _gate = new();
    private StreamWriter? _writer;

    private JsonLinesOperationLog(string path, StreamWriter writer)
    {
        FilePath = path;
        _writer = writer;
    }

    public string FilePath { get; }

    public static JsonLinesOperationLog Create(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            $"peopleapp-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.jsonl");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        return new JsonLinesOperationLog(path, new StreamWriter(stream) { AutoFlush = true });
    }

    public void Write(OperationLogEntry entry)
    {
        lock (_gate)
        {
            if (_writer is null)
            {
                return;
            }
            try
            {
                _writer.WriteLine(JsonSerializer.Serialize(entry, Options));
            }
            catch (IOException)
            {
                // Logging must never break the workflow; stop writing after a failure.
                _writer.Dispose();
                _writer = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
