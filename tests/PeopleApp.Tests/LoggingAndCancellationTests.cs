using System.Net;
using System.Text;
using System.Text.Json;
using PeopleApp.Api;
using PeopleApp.ConsoleUi;
using PeopleApp.Logging;
using Spectre.Console.Testing;

namespace PeopleApp.Tests;

public sealed class LoggingAndCancellationTests
{
    private const string SessionKey = "secret-session-key";
    private const string PersonBody = """{"UserName":"private-user","FirstName":"Private First","Age":77}""";
    private static readonly Uri Root = new($"https://service.test/TripPin/(S({SessionKey}))/");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SuccessfulExchangesLogOperationMethodStatusAndDurationWithoutSensitiveValues()
    {
        var log = new RecordingLog();
        var patched = false;
        using var client = Client((request, _) =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                patched = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path.EndsWith("/People", StringComparison.Ordinal)
                ? Json($$"""{"value":[{{PersonBody}}]}""")
                : path.Contains("missing", StringComparison.Ordinal)
                    ? Json("{}", HttpStatusCode.NotFound)
                    : Json(patched ? """{"UserName":"private-user","FirstName":"Patched Secret"}""" : PersonBody));
        });
        var session = await PeopleServiceSession.OpenAsync(client, Root, Token, log);
        var changes = new PersonChanges();
        changes.SetString(EditablePersonField.FirstName, "Patched Secret");

        await session.GetPeopleAsync(new PeopleQueryOptions([new PersonFilter(PersonFilterField.FirstName, "Filter Secret")]), Token);
        await session.GetPersonAsync("private-user", Token);
        await session.GetPersonAsync("missing", Token);
        await session.UpdatePersonAsync("private-user", changes, Token);

        Assert.Equal(
            ["open_session", "list_people", "get_person", "get_person", "update_person", "update_readback"],
            log.Entries.Select(entry => entry.Operation));
        Assert.Equal(["GET", "GET", "GET", "GET", "PATCH", "GET"], log.Entries.Select(entry => entry.Method));
        Assert.Equal([200, 200, 200, 404, 204, 200], log.Entries.Select(entry => entry.Status));
        Assert.Equal(
            ["succeeded", "succeeded", "succeeded", "not_found", "succeeded", "succeeded"],
            log.Entries.Select(entry => entry.Outcome));
        Assert.All(log.Entries, entry =>
        {
            Assert.Null(entry.FailureStage);
            Assert.Null(entry.ErrorType);
            Assert.True(entry.DurationMs >= 0);
        });
        AssertNoSensitiveValues(log);
    }

    [Theory]
    [InlineData("status", "failed", "status", 500, "PeopleApiException")]
    [InlineData("malformed", "failed", "response", 200, "InvalidDataException")]
    [InlineData("transport", "failed", "send", null, "HttpRequestException")]
    [InlineData("timeout", "timed_out", "send", null, "TaskCanceledException")]
    [InlineData("cancelled", "cancelled", "send", null, "TaskCanceledException")]
    public async Task FailuresLogStageAndDistinguishTimeoutFromUserCancellation(
        string failure, string outcome, string stage, int? status, string errorType)
    {
        var log = new RecordingLog();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var client = Client(async (request, token) =>
        {
            switch (failure)
            {
                case "status":
                    return Json("""{"error":{"message":"private-user"}}""", HttpStatusCode.InternalServerError);
                case "malformed":
                    return Json("{");
                case "transport":
                    throw new HttpRequestException($"Connection to {Root} failed");
                case "cancelled":
                    await cancellation.CancelAsync();
                    break;
            }
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        }, failure == "timeout" ? TimeSpan.FromMilliseconds(100) : Timeout.InfiniteTimeSpan);
        var session = await PeopleServiceSession.OpenAsync(client, Root, Token, log);

        await Assert.ThrowsAnyAsync<Exception>(() => session.GetPeopleAsync(cancellation.Token));

        var entry = log.Entries[^1];
        Assert.Equal("list_people", entry.Operation);
        Assert.Equal(outcome, entry.Outcome);
        Assert.Equal(stage, entry.FailureStage);
        Assert.Equal(status, entry.Status);
        Assert.Equal(errorType, entry.ErrorType);
        AssertNoSensitiveValues(log);
    }

    [Fact]
    public async Task UserCancellationDuringPatchPropagatesWithoutRetryOrReadBack()
    {
        var log = new RecordingLog();
        var requests = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var client = Client(async (request, token) =>
        {
            requests++;
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        });
        var session = await PeopleServiceSession.OpenAsync(client, Root, Token, log);
        var changes = new PersonChanges();
        changes.SetString(EditablePersonField.FirstName, "Patched Secret");

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.UpdatePersonAsync("private-user", changes, cancellation.Token));

        Assert.IsNotType<PersonUpdateException>(exception);
        Assert.Equal(1, requests);
        var entry = log.Entries[^1];
        Assert.Equal(("update_person", "PATCH", "cancelled", "send"), (entry.Operation, entry.Method, entry.Outcome, entry.FailureStage));
    }

    [Fact]
    public async Task PatchTimeoutRemainsAnUncertainUpdateFailureNotACancellation()
    {
        var log = new RecordingLog();
        using var client = Client(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        }, TimeSpan.FromMilliseconds(100));
        var session = await PeopleServiceSession.OpenAsync(client, Root, Token, log);
        var changes = new PersonChanges();
        changes.SetString(EditablePersonField.FirstName, "Patched Secret");

        var exception = await Assert.ThrowsAsync<PersonUpdateException>(
            () => session.UpdatePersonAsync("private-user", changes, Token));

        Assert.False(exception.Acknowledged);
        Assert.Contains("not confirmed", exception.Message);
        Assert.Contains("may have changed", exception.Message);
        Assert.Equal("timed_out", log.Entries[^1].Outcome);
    }

    [Fact]
    public async Task ReadBackMismatchIsLoggedAsVerifyStage()
    {
        var log = new RecordingLog();
        using var client = Client((request, _) => Task.FromResult(request.Method == HttpMethod.Patch
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : Json(PersonBody)));
        var session = await PeopleServiceSession.OpenAsync(client, Root, Token, log);
        var changes = new PersonChanges();
        changes.SetString(EditablePersonField.FirstName, "Patched Secret");

        await Assert.ThrowsAsync<PersonUpdateException>(() => session.UpdatePersonAsync("private-user", changes, Token));

        var entry = log.Entries[^1];
        Assert.Equal(("update_readback", 200, "failed", "verify"), (entry.Operation, entry.Status, entry.Outcome, entry.FailureStage));
        AssertNoSensitiveValues(log);
    }

    [Fact]
    public async Task CancelledFetchStopsWithoutErrorMessageOrRetry()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var requests = 0;
        var run = await RunUiAsync(cancellation, async (_, token) =>
        {
            requests++;
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        }, "1", "1");

        Assert.IsAssignableFrom<OperationCanceledException>(run.Exception);
        Assert.Equal(1, requests);
        Assert.DoesNotContain("timed out", run.Output);
        Assert.DoesNotContain("No automatic retry", run.Output);
        Assert.DoesNotContain("failed", run.Output);
    }

    [Fact]
    public async Task CancellationIsObservedAtTheNextMenuPrompt()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = await RunUiAsync(cancellation, async (_, _) =>
        {
            // Ctrl+C arrives while the successful response is being displayed.
            await cancellation.CancelAsync();
            return Json($$"""{"value":[{{PersonBody}}]}""");
        }, "1", "1", "1");

        Assert.IsAssignableFrom<OperationCanceledException>(run.Exception);
        Assert.Contains("private-user", run.Output);
        Assert.DoesNotContain("Person number", run.Output);
    }

    [Fact]
    public async Task CancelledSaveWarnsThatTheServerMayHaveChangedWithoutRetry()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var patches = 0;
        var run = await RunUiAsync(cancellation, async (request, token) =>
        {
            if (request.Method != HttpMethod.Patch)
            {
                return Json(request.RequestUri!.AbsolutePath.EndsWith("/People", StringComparison.Ordinal)
                    ? $$"""{"value":[{{PersonBody}}]}""" : PersonBody);
            }
            patches++;
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        }, "1", "1", "1", "1", "4", "2", "1", "Changed", "6", "1");

        Assert.IsAssignableFrom<OperationCanceledException>(run.Exception);
        Assert.Equal(1, patches);
        Assert.Contains("may already have applied the change", run.Output);
        Assert.Contains("not retried or rolled back", run.Output);
        Assert.DoesNotContain("Changes saved", run.Output);
        Assert.DoesNotContain("PATCH was not confirmed", run.Output);
    }

    [Fact]
    public void FileLogWritesOneCompactJsonObjectPerLine()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PeopleApp.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string path;
            using (var log = JsonLinesOperationLog.Create(directory))
            {
                path = log.FilePath;
                log.Write(new OperationLogEntry(DateTimeOffset.UnixEpoch, "list_people", "GET", 200, 12, "succeeded", null, null));
                log.Write(new OperationLogEntry(DateTimeOffset.UnixEpoch, "update_person", "PATCH", null, 30, "timed_out", "send", "TaskCanceledException"));
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            using var first = JsonDocument.Parse(lines[0]);
            Assert.Equal("list_people", first.RootElement.GetProperty("operation").GetString());
            Assert.Equal(200, first.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(12, first.RootElement.GetProperty("durationMs").GetInt64());
            Assert.False(first.RootElement.TryGetProperty("failureStage", out _));
            using var second = JsonDocument.Parse(lines[1]);
            Assert.Equal("send", second.RootElement.GetProperty("failureStage").GetString());
            Assert.False(second.RootElement.TryGetProperty("status", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertNoSensitiveValues(RecordingLog log)
    {
        var serialized = JsonSerializer.Serialize(log.Entries);
        foreach (var value in new[] { SessionKey, "service.test", "(S(", "private-user", "Private First", "Patched Secret", "Filter Secret", "missing" })
        {
            Assert.DoesNotContain(value, serialized);
        }
    }

    private static async Task<(Exception? Exception, string Output)> RunUiAsync(
        CancellationTokenSource cancellation,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        params string[] inputs)
    {
        using var client = Client(respond);
        var session = await PeopleServiceSession.OpenAsync(client, Root, Token);
        using var console = new TestConsole();
        console.Profile.Width = 160;
        foreach (var input in inputs)
        {
            console.Input.PushTextWithEnter(input);
        }
        var exception = await Record.ExceptionAsync(
            () => new PeopleConsoleApplication(session, console).RunAsync(cancellation.Token));
        return (exception, console.Output);
    }

    private static HttpClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        TimeSpan? timeout = null) =>
        new(new Handler(send)) { Timeout = timeout ?? Timeout.InfiniteTimeSpan };

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class RecordingLog : IOperationLog
    {
        public List<OperationLogEntry> Entries { get; } = [];

        public void Write(OperationLogEntry entry) => Entries.Add(entry);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = request.RequestUri == Root
                ? Json("""{"value":[{"name":"People"}]}""")
                : await send(request, cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }
}
