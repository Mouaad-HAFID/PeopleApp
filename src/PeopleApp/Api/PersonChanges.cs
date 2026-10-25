using System.Text.Json;

namespace PeopleApp.Api;

public enum EditablePersonField
{
    UserName,
    FirstName,
    LastName,
    MiddleName,
    Age
}

public sealed class PersonChanges
{
    private readonly Dictionary<EditablePersonField, JsonElement> _values = [];

    public bool IsEmpty => _values.Count == 0;

    public void SetString(EditablePersonField field, string? value)
    {
        if (field is not (EditablePersonField.UserName or EditablePersonField.FirstName or
            EditablePersonField.LastName or EditablePersonField.MiddleName))
        {
            throw new ArgumentOutOfRangeException(nameof(field));
        }
        if (value is null && field is EditablePersonField.UserName or EditablePersonField.FirstName)
        {
            throw new ArgumentException($"{field} cannot be null.", nameof(value));
        }
        if (field == EditablePersonField.LastName && value?.Length > 26)
        {
            throw new ArgumentException("LastName cannot exceed 26 characters.", nameof(value));
        }
        _values[field] = JsonSerializer.SerializeToElement(value);
    }

    public void SetAge(long? value) =>
        _values[EditablePersonField.Age] = JsonSerializer.SerializeToElement(value);

    public void KeepUnchanged(EditablePersonField field) => _values.Remove(field);

    public bool TryGetValue(EditablePersonField field, out JsonElement value) =>
        _values.TryGetValue(field, out value);

    internal Dictionary<string, JsonElement> ToPayload() =>
        _values.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value);
}
