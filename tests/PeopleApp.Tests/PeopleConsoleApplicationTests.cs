using System.Net;
using System.Text;
using System.Text.Json;
using PeopleApp.Api;
using PeopleApp.ConsoleUi;
using Spectre.Console.Testing;

namespace PeopleApp.Tests;

public sealed class PeopleConsoleApplicationTests
{
    private const string People = """{"value":[{"UserName":"alice","FirstName":"Alice"}]}""";
    private const string Person = """
        {"@odata.context":"root context","UserName":"alice","Unknown":[null,{"@odata.context":"nested context","Text":"[red]literal[/]"}]}
        """;
    private static readonly Uri Root = new("https://service.test/TripPin/(S(ux-session))/");

    [Fact]
    public async Task MainMenuCanExitOrGoBackWithoutReadingPeople()
    {
        var run = await RunAsync(_ => Json(People), "1", "3", "2");

        Assert.Empty(run.Requests);
        Assert.DoesNotContain("View selected person", run.Output);
    }

    [Fact]
    public async Task NavigationUsesCachedResultsAndDetailsInTheSameSession()
    {
        var run = await RunAsync(
            request => Json(request.RequestUri!.AbsolutePath.EndsWith("/People", StringComparison.Ordinal) ? People : Person),
            "1", "1", // Fetch all.
            "1", "1", // Select Alice.
            "2", "3", // Details -> results -> main.
            "2", "2", "3", // Main -> cached details -> results -> main.
            "3"); // Exit.

        Assert.Equal(new[] { new Uri(Root, "People"), new Uri(Root, "People('alice')") }, run.Requests);
        Assert.Contains("View selected person", run.Output);
        Assert.DoesNotContain("root context", run.Output);
        Assert.Contains("nested context", run.Output);
        Assert.Contains("[red]literal[/]", run.Output);
        Assert.Contains("\"Unknown\"", run.Output);
    }

    [Fact]
    public async Task InvalidMenuAndPersonSelectionsRetryWithoutRefetching()
    {
        var run = await RunAsync(
            _ => Json(People),
            "bad", "1", "1", "1", "0", "99", "bad", "", "3", "2");

        Assert.Single(run.Requests);
        Assert.Contains("Enter one of the menu numbers.", run.Output);
        Assert.Contains("Enter a number from the results table", run.Output);
    }

    [Fact]
    public async Task EmptyResultsAllowChangingQueryAndReturningToMainMenu()
    {
        var run = await RunAsync(_ => Json("""{"value":[]}"""),
            "1", "1", "1", "10", "2", "2");

        Assert.Single(run.Requests);
        Assert.Contains("No people were returned.", run.Output);
        Assert.Contains("Change query", run.Output);
        Assert.Contains("Main menu", run.Output);
    }

    [Fact]
    public async Task QueryRefinementPreservesSettingsAndCancelDiscardsDraft()
    {
        var run = await RunAsync(_ => Json(People),
            "1", "2", // Configure.
            "2", "1", "Al", // FirstName.
            "3", "1", "Why", // LastName.
            "5", "2", // OR.
            "6", "1", "2", // Top.
            "7", "1", "1", // Skip.
            "8", "5", "2", // Age descending.
            "9", // Apply.
            "2", "6", "1", "3", "9", // Refine top only and apply.
            "2", "2", "1", "Discard me", "10", // Draft edit then cancel.
            "2", "9", // Reopen and apply unchanged settings.
            "3", "2");

        Assert.Equal(3, run.Requests.Count);
        Assert.Equal(
            "$filter=contains(FirstName,'Al') or contains(LastName,'Why')&$top=2&$skip=1&$orderby=Age desc",
            Uri.UnescapeDataString(run.Requests[0]!.Query.TrimStart('?')));
        var refined = Uri.UnescapeDataString(run.Requests[1]!.Query.TrimStart('?'));
        Assert.Equal(
            "$filter=contains(FirstName,'Al') or contains(LastName,'Why')&$top=3&$skip=1&$orderby=Age desc",
            refined);
        Assert.Equal(run.Requests[1], run.Requests[2]);
        Assert.DoesNotContain("Discard me", refined);
    }

    [Fact]
    public async Task QuerySettingsCanBeRemovedAndInvalidNumbersRetryLocally()
    {
        var run = await RunAsync(_ => Json(People),
            "1", "2",
            "1", "1", "alice", // Set username.
            "4", "1", "2", // Female.
            "6", "1", "-1", "wrong", "0", // Invalid top then zero.
            "7", "1", "2", // Skip.
            "8", "1", "1", // Sort.
            "9",
            "2", // Refine.
            "1", "2", "4", "2", // Remove filters.
            "6", "2", "7", "2", // Remove top/skip.
            "8", "6", // Remove ordering.
            "9", "3", "2");

        Assert.Equal(2, run.Requests.Count);
        Assert.Contains("$top=0", run.Requests[0]!.Query);
        Assert.Empty(run.Requests[1]!.Query);
        Assert.Contains("Enter a non-negative whole number", run.Output);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("transport")]
    [InlineData("timeout")]
    [InlineData("malformed")]
    public async Task FetchErrorsReturnToMainWithoutAutomaticRetry(string failure)
    {
        var run = await RunAsync(_ => failure switch
        {
            "http" => Json("provider error", HttpStatusCode.ServiceUnavailable),
            "transport" => throw new HttpRequestException("offline"),
            "timeout" => throw new TaskCanceledException("timed out"),
            _ => Json("{")
        }, "1", "1", "2");

        Assert.Single(run.Requests);
        Assert.Contains("No automatic retry was made.", run.Output);
    }

    [Fact]
    public async Task FailedQueryKeepsPreviousResultsAndAppliedSettingsForManualRefinement()
    {
        var reads = 0;
        var run = await RunAsync(_ => ++reads == 2
                ? Json("unavailable", HttpStatusCode.ServiceUnavailable)
                : Json(People),
            "1", "1",
            "2", "6", "1", "4", "9", // Failed refined fetch.
            "2", "9", // Explicitly apply it again.
            "3", "2");

        Assert.Equal(3, run.Requests.Count);
        Assert.Equal("$top=4", run.Requests[1]!.Query.TrimStart('?'));
        Assert.Equal(run.Requests[1], run.Requests[2]);
        Assert.Contains("previous results remain cached", run.Output);
    }

    [Fact]
    public async Task RefreshReadsAgainAndFailedRefreshRetainsExplicitlyStaleDetails()
    {
        var personReads = 0;
        var run = await RunAsync(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/People", StringComparison.Ordinal))
            {
                return Json(People);
            }
            return ++personReads == 2
                ? Json("offline", HttpStatusCode.InternalServerError)
                : Json(personReads == 3 ? """{"UserName":"alice","FirstName":"Refreshed"}""" : Person);
        }, "1", "1", "1", "1", "1", "1", "3", "3");

        Assert.Equal(3, personReads);
        Assert.Equal(4, run.Requests.Count);
        Assert.Contains("last successfully loaded details are still cached", run.Output);
        Assert.Contains("No automatic retry was made.", run.Output);
        Assert.Contains("Refreshed", run.Output);
    }

    [Fact]
    public async Task NestedBackAndCancelActionsDoNotChangeTheQuery()
    {
        var run = await RunAsync(_ => Json(People),
            "1", "2",
            "1", "3", // Filter editor -> back.
            "4", "1", "4", // Gender -> back.
            "5", "3", // Combination -> back.
            "6", "3", // Top editor -> back.
            "7", "1", "", // Skip input -> cancel.
            "8", "7", // Sorting -> back.
            "8", "5", "3", // Direction -> back.
            "9", "3", "2");

        Assert.Single(run.Requests);
        Assert.Empty(run.Requests[0]!.Query);
    }

    [Fact]
    public async Task NotFoundRefreshRemovesOldDetailsButRetainsSelectedKey()
    {
        var personReads = 0;
        var run = await RunAsync(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/People", StringComparison.Ordinal))
            {
                return Json(People);
            }
            return ++personReads == 1 ? Json(Person) : Json("{}", HttpStatusCode.NotFound);
        },
            "1", "1", "1", "1", "1", // Select then refresh.
            "3", "2", // Main -> view selected.
            "3", "3"); // Main -> exit.

        Assert.Equal(3, run.Requests.Count);
        Assert.Contains("No current details are available.", run.Output);
        Assert.Contains("View selected person", run.Output);
    }

    [Fact]
    public async Task PersonReadFailureLeavesResultsUsableAndDoesNotRetry()
    {
        var run = await RunAsync(request =>
                request.RequestUri!.AbsolutePath.EndsWith("/People", StringComparison.Ordinal)
                    ? Json(People)
                    : throw new HttpRequestException("offline"),
            "1", "1", "1", "1", "3", "2");

        Assert.Equal(2, run.Requests.Count);
        Assert.Contains("No automatic retry was made.", run.Output);
        Assert.DoesNotContain("View selected person", run.Output);
    }

    [Fact]
    public async Task MissingSelectedPersonCanReturnToCachedResults()
    {
        var run = await RunAsync(request =>
                Json(request.RequestUri!.AbsolutePath.EndsWith("/People", StringComparison.Ordinal)
                    ? People : "{}", request.RequestUri.AbsolutePath.EndsWith("/People", StringComparison.Ordinal)
                    ? HttpStatusCode.OK : HttpStatusCode.NotFound),
            "1", "1", "1", "1", "3", "2");

        Assert.Equal(2, run.Requests.Count);
        Assert.Contains("Person 'alice' was not found.", run.Output);
    }

    [Fact]
    public async Task EditingCancelAndUnchangedFieldsNeverSendPatch()
    {
        var run = await RunAsync(request => Json(request.RequestUri!.AbsolutePath.EndsWith("/People", StringComparison.Ordinal) ? People : Person),
            "1", "1", "1", "1",
            "4", "2", "1", "Draft", "7", // Draft and cancel.
            "4", "2", "1", "Draft", "2", "2", // Draft then keep unchanged.
            "6", "7", // Save with no changes, then cancel.
            "3", "3");

        Assert.Empty(run.PatchBodies);
        Assert.Equal(2, run.Requests.Count);
        Assert.Contains("No changes selected. Nothing was sent.", run.Output);
    }

    [Fact]
    public async Task EditingValidatesAndSendsOnlyChosenValuesThenDisplaysReadBack()
    {
        var updated = false;
        var run = await RunAsync(request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                updated = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return Json(request.RequestUri!.AbsolutePath.EndsWith("/People", StringComparison.Ordinal)
                ? People : updated
                    ? """{"UserName":"alice","FirstName":"","LastName":null,"MiddleName":"  ","Age":2147483648,"Gender":"Female"}"""
                    : Person);
        },
            "1", "1", "1", "1", "4",
            "2", "1", "", // Literal empty FirstName.
            "3", "1", new string('x', 27), "2", // Reject LastName, back.
            "3", "3", // Clear LastName.
            "4", "1", "  ", // Literal spaces.
            "5", "1", "9223372036854775808", "1.5", "2147483648", // Validate Age.
            "6", "2", // Confirmation back, retain draft.
            "6", "1", // Save and confirm.
            "3", "3");

        Assert.Single(run.PatchBodies);
        using var payload = JsonDocument.Parse(run.PatchBodies[0]);
        Assert.Equal(4, payload.RootElement.EnumerateObject().Count());
        Assert.Equal("", payload.RootElement.GetProperty("FirstName").GetString());
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("LastName").ValueKind);
        Assert.Equal("  ", payload.RootElement.GetProperty("MiddleName").GetString());
        Assert.Equal(2147483648L, payload.RootElement.GetProperty("Age").GetInt64());
        Assert.False(payload.RootElement.TryGetProperty("Gender", out _));
        Assert.Contains("Changes saved and verified by read-back.", run.Output);
        Assert.Contains("Enter an Int64 whole number", run.Output);
        Assert.Equal(4, run.Requests.Count);
    }

    [Fact]
    public async Task VerifiedRenameUpdatesSelectionRefreshAndCachedResultRouting()
    {
        var renamed = false;
        var run = await RunAsync(request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                renamed = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return Json(request.RequestUri!.AbsolutePath.EndsWith("/People", StringComparison.Ordinal)
                ? People : renamed ? """{"UserName":"newkey"}""" : Person);
        },
            "1", "1", "1", "1", "4", "1", "1", "newkey", "6", "1",
            "1", // Refresh.
            "2", "1", "1", // Back to results, select renamed row.
            "3", "3");

        Assert.Single(run.PatchBodies);
        Assert.Equal(6, run.Requests.Count);
        Assert.Equal(new Uri(Root, "People('alice')"), run.Requests[2]);
        Assert.All(run.Requests.Skip(3), uri => Assert.Equal(new Uri(Root, "People('newkey')"), uri));
        Assert.Contains("Cached results may be out of date", run.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedWriteOrReadBackNeverClaimsSuccessOrRetries(bool acknowledged)
    {
        var saved = false;
        var run = await RunAsync(request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                saved = true;
                return acknowledged
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : Json("malformed error", HttpStatusCode.InternalServerError);
            }
            return Json(request.RequestUri!.AbsolutePath.EndsWith("/People", StringComparison.Ordinal)
                ? People : saved ? "{" : Person);
        },
            "1", "1", "1", "1", "4", "1", "1", "newkey", "6", "1", "3", "3");

        Assert.Single(run.PatchBodies);
        Assert.Equal(acknowledged ? 4 : 3, run.Requests.Count);
        Assert.DoesNotContain("Changes saved and verified", run.Output);
        Assert.Contains("Server data may have changed.", run.Output);
        Assert.Contains("No automatic retry or rollback", run.Output);
        Assert.Contains("Rename is unverified", run.Output);
        Assert.Contains("No current details are available.", run.Output);
        if (acknowledged)
        {
            Assert.Contains("acknowledged", run.Output);
        }
        else
        {
            Assert.Contains("HTTP 500", run.Output);
            Assert.Contains("malformed error", run.Output);
        }
    }

    [Fact]
    public async Task SubsequentPatchUsesVerifiedRenamedKey()
    {
        var patches = 0;
        var run = await RunAsync(request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                patches++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return Json(request.RequestUri!.AbsolutePath.EndsWith("/People", StringComparison.Ordinal)
                ? People : patches == 0 ? Person
                : patches == 1 ? """{"UserName":"newkey"}"""
                : """{"UserName":"newkey","FirstName":"Changed"}""");
        },
            "1", "1", "1", "1", "4", "1", "1", "newkey", "6", "1",
            "4", "2", "1", "Changed", "6", "1", "3", "3");

        Assert.Equal(2, run.PatchBodies.Count);
        Assert.Equal(new Uri(Root, "People('alice')"), run.Requests[2]);
        Assert.Equal(new Uri(Root, "People('newkey')"), run.Requests[4]);
        using var secondPayload = JsonDocument.Parse(run.PatchBodies[1]);
        Assert.Single(secondPayload.RootElement.EnumerateObject());
        Assert.Equal("Changed", secondPayload.RootElement.GetProperty("FirstName").GetString());
    }

    [Theory]
    [InlineData("1", "and")]
    [InlineData("2", "or")]
    public async Task IntegratedQueryEditRefreshAndUnrestrictedFetchRetainSessionAndVerifiedKey(
        string combinationChoice, string combinationOperator)
    {
        const string original = """
            {"@odata.context":"root context","UserName":"alice","FirstName":"Alice","LastName":"Whyte","MiddleName":"Before","Age":42,"Gender":"Female","Unknown":[null,{"@odata.context":"nested context"}]}
            """;
        const string updated = """
            {"@odata.context":"root context","UserName":"renamed","FirstName":"Updated","LastName":null,"MiddleName":null,"Age":null,"Gender":"Female","Unknown":[null,{"@odata.context":"nested context"}]}
            """;
        var requestCount = 0;
        var run = await RunAsync(request =>
        {
            var index = requestCount++;
            Assert.Equal(index == 2 ? HttpMethod.Patch : HttpMethod.Get, request.Method);
            return index switch
            {
                0 => Json($$"""{"value":[{{original}}]}"""),
                1 => Json(original),
                2 => new HttpResponseMessage(HttpStatusCode.NoContent),
                3 => Json(updated),
                4 => Json(updated.Replace("\"Unknown\":", "\"RefreshedFromProvider\":", StringComparison.Ordinal)),
                5 => Json($$"""{"value":[{{updated}}]}"""),
                6 => Json(updated),
                _ => throw new InvalidOperationException("Unexpected workflow request.")
            };
        },
            "1", "2", // Configure the initial fetch.
            "2", "1", "Al", "3", "1", "Why",
            "5", combinationChoice,
            "6", "1", "2", "7", "1", "1", "8", "5", "2", "9",
            "1", "1", // Select from filtered results.
            "4", // Edit all five fields, clearing the nullable fields.
            "1", "1", "renamed", "2", "1", "Updated",
            "3", "3", "4", "3", "5", "3", "6", "1",
            "1", // Explicit refresh of the verified new key.
            "2", "3", // Cached results -> main.
            "1", "1", // All people must discard the previous restrictions.
            "1", "1", // Select the renamed person from the fresh list.
            "3", "3"); // Main -> exit.

        Assert.Equal(7, run.Requests.Count);
        Assert.Equal(
            $"$filter=contains(FirstName,'Al') {combinationOperator} contains(LastName,'Why')&$top=2&$skip=1&$orderby=Age desc",
            Uri.UnescapeDataString(run.Requests[0]!.Query.TrimStart('?')));
        Assert.Equal(new Uri(Root, "People('alice')"), run.Requests[1]);
        Assert.Equal(new Uri(Root, "People('alice')"), run.Requests[2]);
        Assert.All(new[] { 3, 4, 6 }, index =>
            Assert.Equal(new Uri(Root, "People('renamed')"), run.Requests[index]));
        Assert.Equal(new Uri(Root, "People"), run.Requests[5]);
        using var payload = JsonDocument.Parse(Assert.Single(run.PatchBodies));
        Assert.Equal(5, payload.RootElement.EnumerateObject().Count());
        Assert.Equal("renamed", payload.RootElement.GetProperty("UserName").GetString());
        Assert.Equal("Updated", payload.RootElement.GetProperty("FirstName").GetString());
        foreach (var property in new[] { "LastName", "MiddleName", "Age" })
        {
            Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty(property).ValueKind);
        }
        Assert.False(payload.RootElement.TryGetProperty("Gender", out _));
        Assert.Contains("Changes saved and verified by read-back.", run.Output);
        Assert.Contains("Cached results may be out of date", run.Output);
        Assert.Contains("RefreshedFromProvider", run.Output);
        Assert.Contains("nested context", run.Output);
        Assert.DoesNotContain("root context", run.Output);
        Assert.DoesNotContain("No automatic retry", run.Output);
    }

    private static async Task<(List<Uri?> Requests, string Output, List<string> PatchBodies)> RunAsync(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        params string[] inputs)
    {
        var requests = new List<Uri?>();
        var patchBodies = new List<string>();
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri == Root)
            {
                return Json("""{"value":[{"name":"People"}]}""");
            }
            requests.Add(request.RequestUri);
            return respond(request);
        }, patchBodies));
        var session = await PeopleServiceSession.OpenAsync(client, Root, TestContext.Current.CancellationToken);
        using var console = new TestConsole();
        console.Profile.Width = 160;
        foreach (var input in inputs)
        {
            console.Input.PushTextWithEnter(input);
        }
        await new PeopleConsoleApplication(session, console).RunAsync(TestContext.Current.CancellationToken);
        return (requests, console.Output, patchBodies);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond, List<string> patchBodies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Patch)
            {
                patchBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            }
            var response = respond(request);
            response.RequestMessage = request;
            return response;
        }
    }
}
