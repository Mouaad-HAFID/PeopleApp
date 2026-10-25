using System.Net;

namespace PeopleApp.Api;

public sealed class PeopleApiException(HttpStatusCode statusCode, string responseBody)
    : Exception($"People API returned HTTP {(int)statusCode} ({statusCode}).")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string ResponseBody { get; } = responseBody;
}
