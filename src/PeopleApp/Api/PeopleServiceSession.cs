using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PeopleApp.Logging;

namespace PeopleApp.Api;

public sealed class PeopleServiceSession
{
    private const string ServiceRootPath = "https://services.odata.org/TripPinRESTierService/";
    private static readonly MediaTypeWithQualityHeaderValue JsonAcceptHeader =
        MediaTypeWithQualityHeaderValue.Parse("application/json;odata.metadata=minimal");

    private readonly HttpClient _httpClient;
    private readonly IOperationLog _log;

    private PeopleServiceSession(HttpClient httpClient, Uri sessionRoot, IOperationLog log)
    {
        _httpClient = httpClient;
        SessionRoot = sessionRoot;
        _log = log;
    }

    public Uri SessionRoot { get; }

    public static async Task<PeopleServiceSession> OpenAsync(
        HttpClient httpClient,
        Uri serviceRoot,
        CancellationToken cancellationToken = default,
        IOperationLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(serviceRoot);
        log ??= NullOperationLog.Instance;

        var exchange = new LoggedExchange(log, "open_session", HttpMethod.Get, cancellationToken);
        try
        {
            using var request = CreateJsonRequest(HttpMethod.Get, serviceRoot);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            var serviceDocument = await ReadSuccessBodyAsync(response, exchange, cancellationToken);
            var sessionRoot = EnsureTrailingSlash(GetResponseUri(response));
            EnsurePeopleEntitySet(serviceDocument);
            exchange.Completed();
            return new PeopleServiceSession(httpClient, sessionRoot, log);
        }
        catch (Exception exception)
        {
            exchange.Failed(exception);
            throw;
        }
    }

    public static Task<PeopleServiceSession> OpenDefaultAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken = default,
        IOperationLog? log = null) =>
        OpenAsync(httpClient, new Uri(ServiceRootPath), cancellationToken, log);

    public async Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(
        CancellationToken cancellationToken = default)
        => await GetPeopleAsync(new PeopleQueryOptions(), cancellationToken);

    public async Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(
        PeopleQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var collectionUri = PeopleQueryBuilder.BuildCollectionUri(SessionRoot, options);
        var exchange = new LoggedExchange(_log, "list_people", HttpMethod.Get, cancellationToken);
        try
        {
            using var request = CreateJsonRequest(HttpMethod.Get, collectionUri);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            var body = await ReadSuccessBodyAsync(response, exchange, cancellationToken);

            using var document = ParseJson(body, "People collection");
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("value", out var values) ||
                values.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("The People response must contain a value array.");
            }

            var people = values.EnumerateArray()
                .Select(PersonSummary.FromJson)
                .ToArray();
            exchange.Completed();
            return people;
        }
        catch (Exception exception)
        {
            exchange.Failed(exception);
            throw;
        }
    }

    public Task<JsonElement?> GetPersonAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userName);
        return GetPersonCoreAsync("get_person", userName, null, cancellationToken);
    }

    private async Task<JsonElement?> GetPersonCoreAsync(
        string operation,
        string userName,
        Action<JsonElement>? verify,
        CancellationToken cancellationToken)
    {
        var personUri = GetPersonUri(userName);
        var exchange = new LoggedExchange(_log, operation, HttpMethod.Get, cancellationToken);
        try
        {
            using var request = CreateJsonRequest(HttpMethod.Get, personUri);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                exchange.Status = (int)response.StatusCode;
                exchange.Completed("not_found");
                return null;
            }

            var body = await ReadSuccessBodyAsync(response, exchange, cancellationToken);
            using var document = ParseJson(body, "person");
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.TryGetProperty("value", out _))
            {
                throw new InvalidDataException("A person response must be a JSON object.");
            }

            if (!document.RootElement.TryGetProperty(nameof(PersonSummary.UserName), out var returnedUserName) ||
                returnedUserName.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("A person response must contain a string UserName.");
            }

            var person = document.RootElement.Clone();
            if (verify is not null)
            {
                exchange.Stage = "verify";
                verify(person);
            }
            exchange.Completed();
            return person;
        }
        catch (Exception exception)
        {
            exchange.Failed(exception);
            throw;
        }
    }

    /// <remarks>
    /// User cancellation propagates as <see cref="OperationCanceledException"/>: once the PATCH may have
    /// been sent, callers must treat the server state as unknown rather than unchanged.
    /// </remarks>
    public async Task<JsonElement> UpdatePersonAsync(
        string userName,
        PersonChanges changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(changes);
        var payload = changes.ToPayload();
        if (payload.Count == 0)
        {
            throw new ArgumentException("At least one intended change is required.", nameof(changes));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var requestedKey = payload.TryGetValue(nameof(PersonSummary.UserName), out var newKey)
            ? newKey.GetString()! : userName;

        var exchange = new LoggedExchange(_log, "update_person", HttpMethod.Patch, cancellationToken);
        try
        {
            using var request = CreateJsonRequest(HttpMethod.Patch, GetPersonUri(userName));
            request.Content = JsonContent.Create(payload);
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            await ReadSuccessBodyAsync(response, exchange, cancellationToken);
            exchange.Completed();
        }
        catch (Exception exception) when (IsRequestFailure(exception))
        {
            exchange.Failed(exception);
            if (IsUserCancellation(exception, cancellationToken))
            {
                throw;
            }
            throw new PersonUpdateException(false, requestedKey, exception);
        }

        try
        {
            return await GetPersonCoreAsync(
                    "update_readback",
                    requestedKey,
                    person => VerifyReadBack(person, payload, requestedKey),
                    cancellationToken) ??
                throw new InvalidDataException("The person was not found during read-back.");
        }
        catch (Exception exception) when (IsRequestFailure(exception) && !IsUserCancellation(exception, cancellationToken))
        {
            throw new PersonUpdateException(true, requestedKey, exception);
        }
    }

    private static void VerifyReadBack(JsonElement person, Dictionary<string, JsonElement> payload, string requestedKey)
    {
        foreach (var (property, expected) in payload)
        {
            if (!person.TryGetProperty(property, out var actual) ||
                !JsonElement.DeepEquals(expected, actual))
            {
                throw new InvalidDataException($"Read-back did not confirm the intended {property} value.");
            }
        }
        if (person.GetProperty(nameof(PersonSummary.UserName)).GetString() != requestedKey)
        {
            throw new InvalidDataException("Read-back returned a different UserName.");
        }
        PersonSummary.FromJson(person);
    }

    private static bool IsUserCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private static bool IsRequestFailure(Exception exception) =>
        exception is PeopleApiException or HttpRequestException or OperationCanceledException or
            InvalidDataException or IOException;

    private Uri GetPersonUri(string userName)
    {
        var literal = userName.Replace("'", "''", StringComparison.Ordinal);
        return new Uri(SessionRoot, $"People('{Uri.EscapeDataString(literal)}')");
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(JsonAcceptHeader);
        return request;
    }

    private static async Task<string> ReadSuccessBodyAsync(
        HttpResponseMessage response,
        LoggedExchange exchange,
        CancellationToken cancellationToken)
    {
        exchange.Status = (int)response.StatusCode;
        exchange.Stage = "read";
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            exchange.Stage = "status";
            throw new PeopleApiException(response.StatusCode, body);
        }

        exchange.Stage = "response";
        return body;
    }

    private static JsonDocument ParseJson(string body, string responseDescription)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The {responseDescription} response was not valid JSON.", exception);
        }
    }

    private static Uri GetResponseUri(HttpResponseMessage response) =>
        response.RequestMessage?.RequestUri ??
        throw new InvalidDataException("The service response did not include its final request URI.");

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var absoluteUri = uri.AbsoluteUri;
        return absoluteUri.EndsWith('/')
            ? uri
            : new Uri(absoluteUri + "/", UriKind.Absolute);
    }

    private static void EnsurePeopleEntitySet(string serviceDocument)
    {
        using var document = ParseJson(serviceDocument, "service document");
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("value", out var value) ||
            value.ValueKind != JsonValueKind.Array ||
            !value.EnumerateArray().Any(
                item => item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("name", out var name) &&
                    name.ValueKind == JsonValueKind.String &&
                    name.GetString() == "People"))
        {
            throw new InvalidDataException("The service document did not advertise a People entity set.");
        }
    }
}
