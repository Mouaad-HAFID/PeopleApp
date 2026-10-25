using System.Text.Json;
using System.Text.Json.Nodes;

namespace PeopleApp.ConsoleUi;

public static class PersonDetailsFormatter
{
    public static string Format(JsonElement person)
    {
        if (person.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Person details must be a JSON object.");
        }

        var details = JsonNode.Parse(person.GetRawText())!.AsObject();
        details.Remove("@odata.context");
        return details.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
