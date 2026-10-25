using System.Net;
using System.Text;
using System.Text.Json;
using PeopleApp.Api;

namespace PeopleApp.Tests;

public sealed class PersonUpdateTests
{
    private static readonly Uri Root = new("https://service.test/TripPin/(S(update-session))/");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(EditablePersonField.UserName, "renamed")]
    [InlineData(EditablePersonField.FirstName, "New first")]
    [InlineData(EditablePersonField.LastName, "O'Brien")]
    [InlineData(EditablePersonField.MiddleName, "New middle")]
    public async Task StringEditsSendOnlyIntendedFieldAndReadBack(EditablePersonField field, string value)
    {
        var changes = new PersonChanges();
        changes.SetString(field, value);
        var requests = new List<(HttpMethod Method, Uri? Uri, string? Body)>();
        using var client = Client(async request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            }
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(Token);
            requests.Add((request.Method, request.RequestUri, body));
            return request.Method == HttpMethod.Patch
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : Json(JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["UserName"] = field == EditablePersonField.UserName ? value : "alice",
                    [field.ToString()] = value,
                    ["Gender"] = "Female",
                    ["Unknown"] = new[] { "retained" }
                }));
        });
        var session = await OpenAsync(client);

        var person = await session.UpdatePersonAsync("alice", changes, Token);

        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Patch, requests[0].Method);
        Assert.Equal(new Uri(Root, "People('alice')"), requests[0].Uri);
        using var payload = JsonDocument.Parse(requests[0].Body!);
        Assert.Single(payload.RootElement.EnumerateObject());
        Assert.Equal(value, payload.RootElement.GetProperty(field.ToString()).GetString());
        Assert.False(payload.RootElement.TryGetProperty("Gender", out _));
        Assert.Equal(new Uri(Root, $"People('{(field == EditablePersonField.UserName ? value : "alice")}')"), requests[1].Uri);
        Assert.Equal("retained", person.GetProperty("Unknown")[0].GetString());
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(2147483648L)]
    [InlineData(-1L)]
    public async Task AgeUsesExactInt64JsonNumber(long age)
    {
        var changes = new PersonChanges();
        changes.SetAge(age);
        using var client = Client(async request =>
        {
            if (request.Method != HttpMethod.Patch)
            {
                return Json($$"""{"UserName":"alice","Age":{{age}}}""");
            }
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(Token));
            Assert.Equal(age, payload.RootElement.GetProperty("Age").GetInt64());
            Assert.Single(payload.RootElement.EnumerateObject());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var session = await OpenAsync(client);

        var person = await session.UpdatePersonAsync("alice", changes, Token);

        Assert.Equal(age, person.GetProperty("Age").GetInt64());
    }

    [Theory]
    [InlineData(EditablePersonField.LastName)]
    [InlineData(EditablePersonField.MiddleName)]
    [InlineData(EditablePersonField.Age)]
    public async Task ExplicitNullIsSentButOtherFieldsAreOmitted(EditablePersonField field)
    {
        var changes = new PersonChanges();
        if (field == EditablePersonField.Age)
        {
            changes.SetAge(null);
        }
        else
        {
            changes.SetString(field, null);
        }
        using var client = Client(async request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(Token));
                Assert.Single(payload.RootElement.EnumerateObject());
                Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty(field.ToString()).ValueKind);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return Json($$"""{"UserName":"alice","{{field}}":null}""");
        });
        var session = await OpenAsync(client);

        await session.UpdatePersonAsync("alice", changes, Token);
    }

    [Fact]
    public async Task MultiFieldPatchPreservesLiteralStringsAndKeepUnchangedRemovesDraft()
    {
        var changes = new PersonChanges();
        changes.SetString(EditablePersonField.UserName, "draft");
        changes.KeepUnchanged(EditablePersonField.UserName);
        changes.SetString(EditablePersonField.FirstName, "");
        changes.SetString(EditablePersonField.MiddleName, "  ");
        changes.SetString(EditablePersonField.LastName, new string('x', 26));
        changes.SetAge(null);
        using var client = Client(async request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(Token));
                Assert.Equal(4, payload.RootElement.EnumerateObject().Count());
                Assert.False(payload.RootElement.TryGetProperty("UserName", out _));
                Assert.False(payload.RootElement.TryGetProperty("Gender", out _));
                Assert.Equal("", payload.RootElement.GetProperty("FirstName").GetString());
                Assert.Equal("  ", payload.RootElement.GetProperty("MiddleName").GetString());
                Assert.Equal(new string('x', 26), payload.RootElement.GetProperty("LastName").GetString());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return Json(JsonSerializer.Serialize(new
            {
                UserName = "alice", FirstName = "", MiddleName = "  ", LastName = new string('x', 26), Age = (long?)null
            }));
        });
        var session = await OpenAsync(client);

        await session.UpdatePersonAsync("alice", changes, Token);
    }

    [Fact]
    public void EstablishedInvalidInputsAreRejectedLocally()
    {
        var changes = new PersonChanges();
        Assert.Throws<ArgumentException>(() => changes.SetString(EditablePersonField.UserName, null));
        Assert.Throws<ArgumentException>(() => changes.SetString(EditablePersonField.FirstName, null));
        Assert.Throws<ArgumentException>(() => changes.SetString(EditablePersonField.LastName, new string('x', 27)));
        Assert.Throws<ArgumentOutOfRangeException>(() => changes.SetString(EditablePersonField.Age, "35"));
        Assert.Throws<ArgumentOutOfRangeException>(() => changes.SetString((EditablePersonField)99, "Gender"));
        Assert.True(changes.IsEmpty);
        Assert.DoesNotContain("Gender", Enum.GetNames<EditablePersonField>());
    }

    [Fact]
    public async Task EmptyChangesNeverSendPatch()
    {
        using var client = Client(_ => throw new InvalidOperationException("No request expected."));
        var session = await OpenAsync(client);

        await Assert.ThrowsAsync<ArgumentException>(() => session.UpdatePersonAsync("alice", new PersonChanges(), Token));
    }

    [Theory]
    [InlineData(400, """{"error":{"code":"","message":"invalid"}}""")]
    [InlineData(422, """{"error":{"message":"too long"}}""")]
    [InlineData(500, """{"error":{"message":"Resource Not Found"}}""")]
    [InlineData(500, "not json")]
    [InlineData(500, "")]
    public async Task FailedPatchPreservesStatusAndBodyWithoutRetryOrReadBack(int status, string body)
    {
        var requests = 0;
        using var client = Client(request =>
        {
            requests++;
            Assert.Equal(HttpMethod.Patch, request.Method);
            return Task.FromResult(Json(body, (HttpStatusCode)status));
        });
        var session = await OpenAsync(client);

        var exception = await Assert.ThrowsAsync<PersonUpdateException>(
            () => session.UpdatePersonAsync("missing", NameChange(), Token));

        Assert.False(exception.Acknowledged);
        var apiException = Assert.IsType<PeopleApiException>(exception.InnerException);
        Assert.Equal((HttpStatusCode)status, apiException.StatusCode);
        Assert.Equal(body, apiException.ResponseBody);
        Assert.Contains("may have changed", exception.Message);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostResponseOrTimeoutReportsUncertaintyWithoutRetry(bool timeout)
    {
        var requests = 0;
        using var client = Client(_ =>
        {
            requests++;
            throw timeout ? new TaskCanceledException("response lost") : new HttpRequestException("response lost");
        });
        var session = await OpenAsync(client);

        var exception = await Assert.ThrowsAsync<PersonUpdateException>(() => session.UpdatePersonAsync("alice", NameChange(), Token));

        Assert.False(exception.Acknowledged);
        Assert.Contains("may have changed", exception.Message);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(404, "{}")]
    [InlineData(500, "read failed")]
    [InlineData(200, "{")]
    [InlineData(200, "{}")]
    [InlineData(200, """{"UserName":"alice","FirstName":"not the intended value"}""")]
    [InlineData(200, """{"UserName":"alice","FirstName":"New first","LastName":42}""")]
    [InlineData(200, """{"UserName":"alice","FirstName":"New first","Gender":{}}""")]
    [InlineData(200, """{"UserName":"alice","FirstName":"New first","Age":[]}""")]
    public async Task AcknowledgedPatchWithFailedOrMismatchedReadBackIsNotVerified(int status, string body)
    {
        var requests = 0;
        using var client = Client(request =>
        {
            requests++;
            return Task.FromResult(request.Method == HttpMethod.Patch
                ? new HttpResponseMessage(HttpStatusCode.NoContent) : Json(body, (HttpStatusCode)status));
        });
        var session = await OpenAsync(client);

        var exception = await Assert.ThrowsAsync<PersonUpdateException>(() => session.UpdatePersonAsync("alice", NameChange(), Token));

        Assert.True(exception.Acknowledged);
        Assert.Contains("acknowledged", exception.Message);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task RenamedKeyAndOriginalKeyAreEscapedForTheirRespectiveRequests()
    {
        var changes = new PersonChanges();
        changes.SetString(EditablePersonField.UserName, "O'Brien/new?");
        var requests = new List<Uri?>();
        using var client = Client(request =>
        {
            requests.Add(request.RequestUri);
            return Task.FromResult(request.Method == HttpMethod.Patch
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : Json("""{"UserName":"O'Brien/new?"}"""));
        });
        var session = await OpenAsync(client);

        await session.UpdatePersonAsync("old'key", changes, Token);

        Assert.EndsWith("People('old%27%27key')", requests[0]!.AbsoluteUri);
        Assert.EndsWith("People('O%27%27Brien%2Fnew%3F')", requests[1]!.AbsoluteUri);
    }

    [Fact]
    public async Task ReadBackTransportFailureIsDistinguishedFromFailedPatch()
    {
        var requests = 0;
        using var client = Client(request =>
        {
            requests++;
            if (request.Method == HttpMethod.Get)
            {
                throw new HttpRequestException("Read-back connection lost");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        var session = await OpenAsync(client);

        var exception = await Assert.ThrowsAsync<PersonUpdateException>(
            () => session.UpdatePersonAsync("alice", NameChange(), Token));

        Assert.True(exception.Acknowledged);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task MissingPersonPatch500AndGet404HaveDifferentOutcomes()
    {
        using var client = Client(request => Task.FromResult(request.Method == HttpMethod.Patch
            ? Json("""{"error":{"message":"Resource Not Found"}}""", HttpStatusCode.InternalServerError)
            : Json("{}", HttpStatusCode.NotFound)));
        var session = await OpenAsync(client);

        var exception = await Assert.ThrowsAsync<PersonUpdateException>(
            () => session.UpdatePersonAsync("missing", NameChange(), Token));
        var person = await session.GetPersonAsync("missing", Token);

        Assert.False(exception.Acknowledged);
        Assert.Equal(HttpStatusCode.InternalServerError, Assert.IsType<PeopleApiException>(exception.InnerException).StatusCode);
        Assert.Null(person);
    }

    private static PersonChanges NameChange()
    {
        var changes = new PersonChanges();
        changes.SetString(EditablePersonField.FirstName, "New first");
        return changes;
    }

    private static Task<PeopleServiceSession> OpenAsync(HttpClient client) =>
        PeopleServiceSession.OpenAsync(client, Root, Token);

    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => new(new Handler(send));

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = request.RequestUri == Root
                ? Json("""{"value":[{"name":"People"}]}""")
                : await send(request);
            response.RequestMessage = request;
            return response;
        }
    }
}
