using System.Text.Json;

namespace PeopleApp.Api;

public sealed record PersonSummary(
    string UserName,
    string? FirstName,
    string? LastName,
    string? Gender,
    string? Age)
{
    internal static PersonSummary FromJson(JsonElement entity)
    {
        if (entity.ValueKind != JsonValueKind.Object ||
            !entity.TryGetProperty(nameof(UserName), out var userName) ||
            userName.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("A person response must contain a string UserName.");
        }

        return new PersonSummary(
            userName.GetString()!,
            ReadOptionalString(entity, nameof(FirstName)),
            ReadOptionalString(entity, nameof(LastName)),
            ReadOptionalString(entity, nameof(Gender)),
            ReadOptionalScalar(entity, nameof(Age)));
    }

    private static string? ReadOptionalString(JsonElement entity, string propertyName)
    {
        if (!entity.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Person property '{propertyName}' must be a string or null.");
        }

        return property.GetString();
    }

    private static string? ReadOptionalScalar(JsonElement entity, string propertyName)
    {
        if (!entity.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
        {
            throw new InvalidDataException($"Person property '{propertyName}' must be a number, string, or null.");
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
    }
}
