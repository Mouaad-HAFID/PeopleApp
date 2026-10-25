using System.Globalization;
using PeopleApp.Api;
using Spectre.Console;

namespace PeopleApp.ConsoleUi;

public sealed partial class PeopleConsoleApplication
{
    private async Task EditPersonAsync(CancellationToken cancellationToken)
    {
        var changes = new PersonChanges();
        while (true)
        {
            console.Write(new Rule("Edit person"));
            console.WriteLine("Gender is read-only. Only intended changes will be sent.");
            foreach (var field in Enum.GetValues<EditablePersonField>())
            {
                var current = _details!.Value.TryGetProperty(field.ToString(), out var value)
                    ? value.GetRawText() : "(not returned)";
                var intended = changes.TryGetValue(field, out var replacement)
                    ? replacement.GetRawText() : "(unchanged)";
                console.WriteLine($"{field}: {current} -> {intended}");
            }

            var action = await ChooseAsync("Edit settings",
                ["UserName", "FirstName", "LastName", "MiddleName", "Age", "Save", "Cancel"]);
            if (action == "Cancel")
            {
                return;
            }
            if (action == "Save")
            {
                if (changes.IsEmpty)
                {
                    console.MarkupLine("[yellow]No changes selected. Nothing was sent.[/]");
                    continue;
                }
                if (await ChooseAsync("Send these changes?", ["Save", "Back"]) == "Save")
                {
                    await SavePersonAsync(changes, cancellationToken);
                    return;
                }
                continue;
            }

            var fieldToEdit = Enum.Parse<EditablePersonField>(action);
            var nullable = fieldToEdit is EditablePersonField.LastName or EditablePersonField.MiddleName or EditablePersonField.Age;
            var edit = await ChooseAsync($"Edit {action}", nullable
                ? ["Replace value", "Keep unchanged", "Clear to null", "Back"]
                : ["Replace value", "Keep unchanged", "Back"]);
            if (edit == "Keep unchanged")
            {
                changes.KeepUnchanged(fieldToEdit);
            }
            else if (edit == "Clear to null")
            {
                if (fieldToEdit == EditablePersonField.Age)
                {
                    changes.SetAge(null);
                }
                else
                {
                    changes.SetString(fieldToEdit, null);
                }
            }
            else if (edit == "Replace value")
            {
                await PromptReplacementAsync(changes, fieldToEdit);
            }
        }
    }

    private async Task PromptReplacementAsync(PersonChanges changes, EditablePersonField field)
    {
        while (true)
        {
            var input = await console.PromptAsync(new TextPrompt<string>(
                field == EditablePersonField.Age
                    ? "Age (Int64 integer; blank to cancel):"
                    : $"{field} (empty and whitespace are literal values):").AllowEmpty(), _cancellationToken);
            if (field == EditablePersonField.Age)
            {
                if (input.Length == 0)
                {
                    return;
                }
                if (long.TryParse(input, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var age))
                {
                    changes.SetAge(age);
                    return;
                }
                console.MarkupLine("[yellow]Enter an Int64 whole number or leave blank to cancel.[/]");
            }
            else
            {
                try
                {
                    changes.SetString(field, input);
                    return;
                }
                catch (ArgumentException exception)
                {
                    console.WriteLine(exception.Message);
                    if (await ChooseAsync("Invalid value", ["Try again", "Back"]) == "Back")
                    {
                        return;
                    }
                }
            }
        }
    }

    private async Task SavePersonAsync(PersonChanges changes, CancellationToken cancellationToken)
    {
        var originalKey = _selectedKey!;
        try
        {
            var person = await console.Status().StartAsync("Saving and verifying person...",
                _ => session.UpdatePersonAsync(originalKey, changes, cancellationToken));
            _selectedKey = person.GetProperty(nameof(PersonSummary.UserName)).GetString()!;
            _details = person;
            _resultsMayBeStale = true;
            if (_results is not null)
            {
                var updatedRow = PersonSummary.FromJson(person);
                _results = _results.Select(row => row.UserName == originalKey
                    ? updatedRow : row).ToArray();
            }
            console.MarkupLine("[green]Changes saved and verified by read-back.[/]");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _details = null;
            _resultsMayBeStale = true;
            console.MarkupLine("[yellow]Save was cancelled before its outcome was confirmed. The server may already have applied the change; it was not retried or rolled back. Inspect current state in a new run.[/]");
            throw;
        }
        catch (PersonUpdateException exception)
        {
            _details = null;
            _resultsMayBeStale = true;
            console.WriteLine(exception.Message);
            if (exception.InnerException is PeopleApiException apiException)
            {
                console.WriteLine($"HTTP {(int)apiException.StatusCode} ({apiException.StatusCode})");
                console.WriteLine(apiException.ResponseBody);
            }
            else
            {
                console.WriteLine(exception.InnerException!.Message);
            }
            if (exception.RequestedKey != originalKey)
            {
                console.WriteLine($"Requested new UserName: '{exception.RequestedKey}'. Rename is unverified; selection still uses '{originalKey}'. Fetch people to select the actual current key.");
            }
            console.MarkupLine("[yellow]Server data may have changed. No automatic retry or rollback was made. Refresh or fetch people to inspect current state.[/]");
        }
    }

}
