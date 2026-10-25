using System.Net;
using System.Text;
using System.Text.Json;
using PeopleApp.Api;
using PeopleApp.ConsoleUi;

namespace PeopleApp.Tests;

public sealed class PeopleServiceSessionTests
{
    private static readonly Uri ServiceRoot = new("https://service.test/TripPin/(S(test-key))/");
    private const string ServiceDocument = """
        {"value":[{"name":"People","kind":"EntitySet","url":"People"}]}
        """;

    [Fact]
    public async Task SessionRetainsNewlyAcquiredRootForCollectionAndPersonReads()
    {
        var initialRoot = new Uri("https://service.test/TripPin/");
        var acquiredRoot = new Uri("https://service.test/TripPin/(S(new-session-key))/");
        using var finalRequest = new HttpRequestMessage(HttpMethod.Get, acquiredRoot);
        var requests = new List<Uri?>();
        using var client = new HttpClient(new DelegateHandler((request, _) =>
        {
            requests.Add(request.RequestUri);
            if (request.RequestUri == initialRoot)
            {
                var serviceResponse = JsonResponse(ServiceDocument);
                serviceResponse.RequestMessage = finalRequest;
                return Task.FromResult(serviceResponse);
            }

            return Task.FromResult(request.RequestUri == new Uri(acquiredRoot, "People")
                ? JsonResponse("""{"value":[{"UserName":"alice"}]}""")
                : JsonResponse("""{"UserName":"alice","Age":null}"""));
        }));

        var session = await PeopleServiceSession.OpenAsync(client, initialRoot, TestToken);
        var people = await session.GetPeopleAsync(TestToken);
        var person = await session.GetPersonAsync(people[0].UserName, TestToken);

        Assert.Equal(acquiredRoot, session.SessionRoot);
        Assert.Single(people);
        Assert.NotNull(person);
        Assert.Equal(
            new[] { initialRoot, new Uri(acquiredRoot, "People"), new Uri(acquiredRoot, "People('alice')") },
            requests);
    }

    [Fact]
    public async Task EmptyCollectionReturnsEmptyList()
    {
        using var client = CreateClient(_ => JsonResponse("""{"value":[]}"""));
        var session = await OpenSessionAsync(client);

        var people = await session.GetPeopleAsync(TestToken);

        Assert.Empty(people);
    }

    [Fact]
    public async Task PersonKeyEscapesODataQuotesAndUriCharacters()
    {
        Uri? requestedUri = null;
        using var client = CreateClient(request =>
        {
            requestedUri = request.RequestUri;
            return JsonResponse("""{"UserName":"O'Brien/A?B"}""");
        });
        var session = await OpenSessionAsync(client);

        await session.GetPersonAsync("O'Brien/A?B", TestToken);

        Assert.EndsWith(
            "People('O%27%27Brien%2FA%3FB')",
            requestedUri!.AbsoluteUri,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingPersonReturnsNullOnNotFound()
    {
        using var client = CreateClient(_ => JsonResponse("{}", HttpStatusCode.NotFound));
        var session = await OpenSessionAsync(client);

        var person = await session.GetPersonAsync("missing", TestToken);

        Assert.Null(person);
    }

    [Fact]
    public async Task CollectionRejectsEntityShapeAndMalformedJson()
    {
        using var client = CreateClient(_ => JsonResponse("""{"UserName":"alice"}"""));
        var session = await OpenSessionAsync(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => session.GetPeopleAsync(TestToken));

        using var malformedClient = CreateClient(_ => JsonResponse("{"));
        var malformedSession = await OpenSessionAsync(malformedClient);
        await Assert.ThrowsAsync<InvalidDataException>(() => malformedSession.GetPeopleAsync(TestToken));
    }

    [Fact]
    public async Task PersonRejectsCollectionShape()
    {
        using var client = CreateClient(_ => JsonResponse("""{"value":[]}"""));
        var session = await OpenSessionAsync(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => session.GetPersonAsync("alice", TestToken));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"FirstName":"Alice"}""")]
    [InlineData("""{"UserName":null}""")]
    [InlineData("""{"UserName":123}""")]
    public async Task PersonRejectsMissingOrNonStringUserName(string body)
    {
        using var client = CreateClient(_ => JsonResponse(body));
        var session = await OpenSessionAsync(client);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => session.GetPersonAsync("alice", TestToken));

        Assert.Equal("A person response must contain a string UserName.", exception.Message);
    }

    [Fact]
    public async Task HttpFailureIsNotReportedAsEmptyResults()
    {
        using var client = CreateClient(_ => JsonResponse("server failure", HttpStatusCode.InternalServerError));
        var session = await OpenSessionAsync(client);

        var exception = await Assert.ThrowsAsync<PeopleApiException>(() => session.GetPeopleAsync(TestToken));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal("server failure", exception.ResponseBody);
    }

    [Fact]
    public async Task TransportFailureIsPropagated()
    {
        using var client = CreateClient(_ => throw new HttpRequestException("connection failed"));
        var session = await OpenSessionAsync(client);

        await Assert.ThrowsAsync<HttpRequestException>(() => session.GetPeopleAsync(TestToken));
    }

    [Fact]
    public async Task TimedOutRequestIsPropagated()
    {
        using var client = new HttpClient(new DelegateHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri == ServiceRoot)
            {
                return JsonResponse(ServiceDocument);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("""{"value":[]}""");
        }))
        {
            Timeout = TimeSpan.FromMilliseconds(100)
        };
        var session = await OpenSessionAsync(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.GetPeopleAsync(TestToken));
    }

    [Theory]
    [InlineData("service")]
    [InlineData("collection")]
    [InlineData("person")]
    public async Task TimeoutIncludesBodyDownloadAfterHeadersAreReturned(string operation)
    {
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        watchdog.CancelAfter(TimeSpan.FromSeconds(5));
        using var stalledContent = new StalledContent();
        using var client = new HttpClient(new DelegateHandler((request, _) =>
            Task.FromResult(operation != "service" && request.RequestUri == ServiceRoot
                ? JsonResponse(ServiceDocument)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = stalledContent })))
        {
            Timeout = TimeSpan.FromMilliseconds(250)
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            var session = await PeopleServiceSession.OpenAsync(client, ServiceRoot, watchdog.Token);
            if (operation == "collection")
            {
                await session.GetPeopleAsync(watchdog.Token);
            }
            else if (operation == "person")
            {
                await session.GetPersonAsync("alice", watchdog.Token);
            }
        });

        Assert.True(stalledContent.DownloadStarted);
        Assert.False(watchdog.IsCancellationRequested, "HttpClient.Timeout must cancel the stalled download before the watchdog.");
    }

    [Theory]
    [InlineData("Whyte", "$filter=contains(LastName,'Whyte')")]
    [InlineData("whyte", "$filter=contains(LastName,'whyte')")]
    [InlineData("O'Brien & $filter=x", "$filter=contains(LastName,'O''Brien & $filter=x')")]
    public async Task LastNameFilterUsesCasePreservingEscapedContains(string value, string expectedQuery)
    {
        var requestUri = await GetCollectionRequestUriAsync(
            new PeopleQueryOptions([new PersonFilter(PersonFilterField.LastName, value)]));

        Assert.Equal(expectedQuery, Uri.UnescapeDataString(requestUri.Query.TrimStart('?')));
        Assert.Single(requestUri.Query.TrimStart('?').Split('&'));
    }

    [Fact]
    public async Task FilterQuotesAndUriCharactersRemainLiteralData()
    {
        Uri? requestedUri = null;
        using var client = CreateClient(request =>
        {
            requestedUri = request.RequestUri;
            return JsonResponse("""{"value":[]}""");
        });
        var session = await OpenSessionAsync(client);
        var options = new PeopleQueryOptions(
            [new PersonFilter(PersonFilterField.UserName, "O'Brien & $filter=x")]);

        var people = await session.GetPeopleAsync(options, TestToken);
        var query = requestedUri!.Query;
        var decodedQuery = Uri.UnescapeDataString(query.TrimStart('?'));

        Assert.Empty(people);
        Assert.Equal("$filter=UserName eq 'O''Brien & $filter=x'", decodedQuery);
        Assert.Contains("%26", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%24", query, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(FilterCombination.And, "$filter=contains(FirstName,'Scott') and contains(LastName,'Ketchum') and Gender eq Trippin.PersonGender'Female'")]
    [InlineData(FilterCombination.Or, "$filter=contains(FirstName,'Scott') or contains(LastName,'Ketchum') or Gender eq Trippin.PersonGender'Female'")]
    public async Task FiltersUseSelectedCombinationAndTypedEnumLiteral(
        FilterCombination combination,
        string expectedQuery)
    {
        var requestUri = await GetCollectionRequestUriAsync(
            new PeopleQueryOptions(
                [
                    new PersonFilter(PersonFilterField.FirstName, "Scott"),
                    new PersonFilter(PersonFilterField.LastName, "Ketchum"),
                    new PersonFilter(PersonFilterField.Gender, "Female")
                ],
                combination));

        Assert.Equal(expectedQuery, Uri.UnescapeDataString(requestUri.Query.TrimStart('?')));
    }

    [Fact]
    public async Task InvalidGenderFilterIsRejectedBeforeRequest()
    {
        var collectionRequestCount = 0;
        using var client = CreateClient(_ =>
        {
            collectionRequestCount++;
            return JsonResponse("""{"value":[]}""");
        });
        var session = await OpenSessionAsync(client);
        var options = new PeopleQueryOptions(
            [new PersonFilter(PersonFilterField.Gender, "0")]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => session.GetPeopleAsync(options, TestToken));

        Assert.Equal(0, collectionRequestCount);
    }

    [Fact]
    public async Task OmittedSlicingAndOrderingLeaveTheCollectionUnchanged()
    {
        var requestUri = await GetCollectionRequestUriAsync(new PeopleQueryOptions());

        Assert.Empty(requestUri.Query);
    }

    [Fact]
    public async Task ZeroTopAndSkipAreIncludedExplicitly()
    {
        var requestUri = await GetCollectionRequestUriAsync(
            new PeopleQueryOptions(top: 0, skip: 0));

        Assert.Equal("$top=0&$skip=0", Uri.UnescapeDataString(requestUri.Query.TrimStart('?')));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, -1)]
    public async Task NegativeSlicingValuesAreRejectedBeforeRequest(int? top, int? skip)
    {
        var collectionRequestCount = 0;
        using var client = CreateClient(_ =>
        {
            collectionRequestCount++;
            return JsonResponse("""{"value":[]}""");
        });
        var session = await OpenSessionAsync(client);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => session.GetPeopleAsync(new PeopleQueryOptions(top: top, skip: skip), TestToken));

        Assert.Equal(0, collectionRequestCount);
    }

    [Theory]
    [InlineData(PersonSortField.UserName, SortDirection.Ascending, "$orderby=UserName asc")]
    [InlineData(PersonSortField.UserName, SortDirection.Descending, "$orderby=UserName desc")]
    [InlineData(PersonSortField.FirstName, SortDirection.Ascending, "$orderby=FirstName asc")]
    [InlineData(PersonSortField.FirstName, SortDirection.Descending, "$orderby=FirstName desc")]
    [InlineData(PersonSortField.LastName, SortDirection.Ascending, "$orderby=LastName asc")]
    [InlineData(PersonSortField.LastName, SortDirection.Descending, "$orderby=LastName desc")]
    [InlineData(PersonSortField.Gender, SortDirection.Ascending, "$orderby=Gender asc")]
    [InlineData(PersonSortField.Gender, SortDirection.Descending, "$orderby=Gender desc")]
    [InlineData(PersonSortField.Age, SortDirection.Ascending, "$orderby=Age asc")]
    [InlineData(PersonSortField.Age, SortDirection.Descending, "$orderby=Age desc")]
    public async Task SortSelectionUsesOneCanonicalFieldAndDirection(
        PersonSortField field,
        SortDirection direction,
        string expectedQuery)
    {
        var requestUri = await GetCollectionRequestUriAsync(
            new PeopleQueryOptions(sortField: field, sortDirection: direction));

        Assert.Equal(expectedQuery, Uri.UnescapeDataString(requestUri.Query.TrimStart('?')));
    }

    [Fact]
    public async Task FilterSliceAndOrderAreCombinedInSingleCollectionRequest()
    {
        Uri? requestUri = null;
        using var client = CreateClient(request =>
        {
            requestUri = request.RequestUri;
            return JsonResponse("""{"value":[{"UserName":"alice"}]}""");
        });
        var session = await OpenSessionAsync(client);
        var options = new PeopleQueryOptions(
            [new PersonFilter(PersonFilterField.FirstName, "Scott")],
            FilterCombination.And,
            top: 2,
            skip: 4,
            sortField: PersonSortField.UserName,
            sortDirection: SortDirection.Descending);

        var people = await session.GetPeopleAsync(options, TestToken);

        Assert.Equal(["alice"], people.Select(person => person.UserName));
        Assert.Equal(
            "$filter=contains(FirstName,'Scott')&$top=2&$skip=4&$orderby=UserName desc",
            Uri.UnescapeDataString(requestUri!.Query.TrimStart('?')));
    }

    [Fact]
    public void FullPersonDetailsOnlyOmitTopLevelContext()
    {
        using var document = JsonDocument.Parse("""
            {
              "@odata.context": "https://service.test/$metadata#People/$entity",
              "@odata.type": "#Trippin.Manager",
              "UserName": "alice",
              "Age": null,
              "Emails": ["a@example.test"],
              "AddressInfo": [{"Address": "Main St", "City": {"Name": "Town"}}],
              "Nested": {"@odata.context": "keep-me", "Value": 1}
            }
            """);

        using var formatted = JsonDocument.Parse(PersonDetailsFormatter.Format(document.RootElement));
        var details = formatted.RootElement;

        Assert.False(details.TryGetProperty("@odata.context", out _));
        Assert.Equal("#Trippin.Manager", details.GetProperty("@odata.type").GetString());
        Assert.Equal(JsonValueKind.Null, details.GetProperty("Age").ValueKind);
        Assert.Equal("a@example.test", details.GetProperty("Emails")[0].GetString());
        Assert.Equal("Town", details.GetProperty("AddressInfo")[0].GetProperty("City").GetProperty("Name").GetString());
        Assert.Equal("keep-me", details.GetProperty("Nested").GetProperty("@odata.context").GetString());
    }

    [Fact]
    public async Task ServiceDocumentMustAdvertisePeople()
    {
        using var client = CreateClient(
            _ => JsonResponse("""{"value":[{"name":"Airports"}]}"""),
            """{"value":[{"name":"Airports"}]}""");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => PeopleServiceSession.OpenAsync(client, ServiceRoot, TestToken));
    }

    private static async Task<PeopleServiceSession> OpenSessionAsync(HttpClient client) =>
        await PeopleServiceSession.OpenAsync(client, ServiceRoot, TestToken);

    private static async Task<Uri> GetCollectionRequestUriAsync(PeopleQueryOptions options)
    {
        Uri? requestUri = null;
        using var client = CreateClient(request =>
        {
            requestUri = request.RequestUri;
            return JsonResponse("""{"value":[]}""");
        });
        var session = await OpenSessionAsync(client);
        await session.GetPeopleAsync(options, TestToken);
        return requestUri!;
    }

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        string serviceDocument = ServiceDocument) =>
        new(new DelegateHandler((request, _) => Task.FromResult(
            request.RequestUri == ServiceRoot
                ? JsonResponse(serviceDocument)
                : responseFactory(request))));

    private static HttpResponseMessage JsonResponse(
        string content,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private sealed class StalledContent : HttpContent
    {
        public bool DownloadStarted { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            DownloadStarted = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            SendAndAttachRequestAsync(request, cancellationToken);

        private async Task<HttpResponseMessage> SendAndAttachRequestAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await send(request, cancellationToken);
            response.RequestMessage ??= request;
            return response;
        }
    }
}
