namespace PeopleApp.Api;

public sealed class PersonUpdateException(bool acknowledged, string requestedKey, Exception innerException)
    : Exception(
        acknowledged
            ? "The service acknowledged the PATCH, but its result could not be verified."
            : "The PATCH was not confirmed: the service returned an error or the response was lost. Server data may have changed.",
        innerException)
{
    public bool Acknowledged { get; } = acknowledged;

    public string RequestedKey { get; } = requestedKey;
}
