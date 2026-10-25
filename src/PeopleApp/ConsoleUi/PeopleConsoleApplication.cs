using System.Globalization;
using System.Text.Json;
using PeopleApp.Api;
using Spectre.Console;

namespace PeopleApp.ConsoleUi;

public sealed partial class PeopleConsoleApplication(PeopleServiceSession session, IAnsiConsole console)
{
    private PeopleQueryOptions _query = new();
    private IReadOnlyList<PersonSummary>? _results;
    private string? _selectedKey;
    private JsonElement? _details;
    private bool _resultsMayBeStale;
    private CancellationToken _cancellationToken;

    /// <summary>
    /// Runs until Exit. Cancellation propagates as <see cref="OperationCanceledException"/> so the
    /// host can stop cleanly; reads are never retried automatically.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = await ChooseAsync("Main menu",
                _selectedKey is null
                    ? ["Fetch people", "Exit"]
                    : ["Fetch people", "View selected person", "Exit"]);
            if (action == "Exit")
            {
                return;
            }

            if (action == "View selected person")
            {
                if (await ShowDetailsAsync(cancellationToken))
                {
                    await ShowResultsAsync(cancellationToken);
                }
            }
            else
            {
                await FetchMenuAsync(cancellationToken);
            }
        }
    }

    private async Task FetchMenuAsync(CancellationToken cancellationToken)
    {
        var action = await ChooseAsync("Fetch people", ["All people", "Configure query", "Back"]);
        if (action == "Back")
        {
            return;
        }

        var query = action == "All people" ? new PeopleQueryOptions() : await ConfigureQueryAsync();
        if (query is not null && await FetchAsync(query, cancellationToken))
        {
            await ShowResultsAsync(cancellationToken);
        }
    }

    private async Task<bool> FetchAsync(PeopleQueryOptions query, CancellationToken cancellationToken)
    {
        _query = query;
        var succeeded = await ConsoleReadOperation.TryRunAsync(console, "Fetching people...", async () =>
        {
            var people = await session.GetPeopleAsync(query, cancellationToken);
            _results = people;
            _resultsMayBeStale = false;
        }, cancellationToken);
        if (!succeeded && _results is not null)
        {
            console.MarkupLine("[yellow]The previous results remain cached; they do not reflect the failed query.[/]");
        }
        return succeeded;
    }

    private async Task ShowResultsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            DisplayResults();
            var action = await ChooseAsync("Results", _results is { Count: > 0 }
                ? ["Select person", "Change query", "Main menu"]
                : ["Change query", "Main menu"]);
            if (action == "Main menu")
            {
                return;
            }

            if (action == "Change query")
            {
                var query = await ConfigureQueryAsync();
                if (query is not null)
                {
                    await FetchAsync(query, cancellationToken);
                }
            }
            else
            {
                var number = await PromptPersonNumberAsync();
                if (number is null)
                {
                    continue;
                }

                var key = _results![number.Value - 1].UserName;
                if (await ReadPersonAsync(key, cancellationToken) &&
                    !await ShowDetailsAsync(cancellationToken))
                {
                    return;
                }
            }
        }
    }

    private async Task<bool> ShowDetailsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            console.Write(new Rule("Selected person"));
            console.WriteLine($"UserName: {_selectedKey}");
            if (_details is not null)
            {
                console.WriteLine(PersonDetailsFormatter.Format(_details.Value));
            }
            else
            {
                console.MarkupLine("[yellow]No current details are available. Refresh to read this person.[/]");
            }

            var actions = new List<string> { "Refresh" };
            if (_results is not null)
            {
                actions.Add("Back to results");
            }
            actions.Add("Main menu");
            if (_details is not null)
            {
                actions.Add("Edit person");
            }
            var action = await ChooseAsync("Details", actions);
            if (action == "Edit person")
            {
                await EditPersonAsync(cancellationToken);
                continue;
            }
            if (action != "Refresh")
            {
                return action == "Back to results";
            }

            await ReadPersonAsync(_selectedKey!, cancellationToken);
        }
    }

    private async Task<bool> ReadPersonAsync(string key, CancellationToken cancellationToken)
    {
        JsonElement? person = null;
        var succeeded = await ConsoleReadOperation.TryRunAsync(console, "Reading person...", async () =>
        {
            person = await session.GetPersonAsync(key, cancellationToken);
        }, cancellationToken);
        if (!succeeded)
        {
            if (_details is not null)
            {
                console.MarkupLine("[yellow]The last successfully loaded details are still cached; they have not been refreshed.[/]");
            }
            return false;
        }

        if (person is null)
        {
            console.WriteLine($"Person '{key}' was not found.");
            if (_selectedKey == key)
            {
                _details = null;
            }
            return false;
        }

        _selectedKey = key;
        _details = person;
        return true;
    }

    private void DisplayResults()
    {
        console.Write(new Rule("People"));
        if (_resultsMayBeStale)
        {
            console.MarkupLine("[yellow]Cached results may be out of date after an update. Fetch again to reapply filtering and ordering.[/]");
        }
        if (_results is not { Count: > 0 })
        {
            console.MarkupLine("[yellow]No people were returned. Change the query or return to the main menu.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("#")
            .AddColumn("UserName")
            .AddColumn("First name")
            .AddColumn("Last name")
            .AddColumn("Gender")
            .AddColumn("Age");
        for (var index = 0; index < _results.Count; index++)
        {
            var person = _results[index];
            table.AddRow(
                (index + 1).ToString(CultureInfo.InvariantCulture),
                Markup.Escape(person.UserName),
                Markup.Escape(person.FirstName ?? ""),
                Markup.Escape(person.LastName ?? ""),
                Markup.Escape(person.Gender ?? ""),
                Markup.Escape(person.Age ?? ""));
        }
        console.Write(table);
    }

    private async Task<int?> PromptPersonNumberAsync()
    {
        while (true)
        {
            var input = await console.PromptAsync(new TextPrompt<string>("Person number (blank to cancel):").AllowEmpty(), _cancellationToken);
            if (input.Length == 0)
            {
                return null;
            }
            if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
                number >= 1 && number <= _results!.Count)
            {
                return number;
            }
            console.MarkupLine("[yellow]Enter a number from the results table, or leave blank to cancel.[/]");
        }
    }

    private async Task<PeopleQueryOptions?> ConfigureQueryAsync()
    {
        var filters = _query.Filters.ToList();
        var combination = _query.Combination;
        var top = _query.Top;
        var skip = _query.Skip;
        var sortField = _query.SortField;
        var sortDirection = _query.SortDirection;

        while (true)
        {
            console.Write(new Rule("Configure query"));
            foreach (var filterField in Enum.GetValues<PersonFilterField>())
            {
                var filter = filters.Find(item => item.Field == filterField);
                console.WriteLine($"{filterField}: {(filter is null ? "(not set)" : $"'{filter.Value}'")}");
            }
            console.WriteLine($"Combination: {combination}; Top: {top?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}; Skip: {skip?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}");
            console.WriteLine($"Sort: {(sortField is null ? "(none)" : $"{sortField} {sortDirection}")}");
            console.WriteLine("UserName is exact; FirstName/LastName use case-sensitive contains.");

            var action = await ChooseAsync("Query settings",
                ["UserName", "FirstName", "LastName", "Gender", "AND/OR", "Top", "Skip", "Sorting", "Apply", "Cancel"]);
            if (action == "Cancel")
            {
                return null;
            }
            if (action == "Apply")
            {
                return new PeopleQueryOptions(filters, combination, top, skip, sortField, sortDirection);
            }

            if (Enum.TryParse<PersonFilterField>(action, out var field))
            {
                var edit = await ChooseAsync($"Edit {field}", ["Set value", "Remove filter", "Back"]);
                if (edit == "Remove filter")
                {
                    filters.RemoveAll(item => item.Field == field);
                }
                else if (edit == "Set value")
                {
                    string value;
                    if (field == PersonFilterField.Gender)
                    {
                        value = await ChooseAsync("Gender", ["Male", "Female", "Unknown", "Back"]);
                        if (value == "Back")
                        {
                            continue;
                        }
                    }
                    else
                    {
                        value = await console.PromptAsync(new TextPrompt<string>($"Value for {field} (empty is a literal empty string):").AllowEmpty(), _cancellationToken);
                    }
                    filters.RemoveAll(item => item.Field == field);
                    filters.Add(new PersonFilter(field, value));
                }
            }
            else if (action == "AND/OR")
            {
                var mode = await ChooseAsync("Combine filters", ["AND (all criteria)", "OR (any criterion)", "Back"]);
                if (mode != "Back")
                {
                    combination = mode.StartsWith("AND", StringComparison.Ordinal)
                        ? FilterCombination.And : FilterCombination.Or;
                }
            }
            else if (action is "Top" or "Skip")
            {
                var edit = await ChooseAsync($"Edit {action}", ["Set value", "Remove limit/offset", "Back"]);
                if (edit == "Back")
                {
                    continue;
                }
                var value = edit == "Set value" ? await PromptNonNegativeIntegerAsync(action) : null;
                if (edit == "Set value" && value is null)
                {
                    continue;
                }
                if (action == "Top")
                {
                    top = value;
                }
                else
                {
                    skip = value;
                }
            }
            else if (action == "Sorting")
            {
                var selected = await ChooseAsync("Sort field", ["UserName", "FirstName", "LastName", "Gender", "Age", "None", "Back"]);
                if (selected == "None")
                {
                    sortField = null;
                }
                else if (selected != "Back")
                {
                    var direction = await ChooseAsync("Sort direction", ["Ascending", "Descending", "Back"]);
                    if (direction != "Back")
                    {
                        sortField = Enum.Parse<PersonSortField>(selected);
                        sortDirection = Enum.Parse<SortDirection>(direction);
                    }
                }
            }
        }
    }

    private async Task<int?> PromptNonNegativeIntegerAsync(string label)
    {
        while (true)
        {
            var input = await console.PromptAsync(new TextPrompt<string>($"{label} (blank to cancel):").AllowEmpty(), _cancellationToken);
            if (input.Length == 0)
            {
                return null;
            }
            if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
            console.MarkupLine("[yellow]Enter a non-negative whole number, or leave blank to cancel.[/]");
        }
    }

    private async Task<string> ChooseAsync(string title, IReadOnlyList<string> choices)
    {
        console.Write(new Rule(Markup.Escape(title)));
        for (var index = 0; index < choices.Count; index++)
        {
            console.WriteLine($"{index + 1}. {choices[index]}");
        }
        while (true)
        {
            var input = await console.PromptAsync(new TextPrompt<string>("Choose an option:").AllowEmpty(), _cancellationToken);
            if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
                number >= 1 && number <= choices.Count)
            {
                return choices[number - 1];
            }
            console.MarkupLine("[yellow]Enter one of the menu numbers.[/]");
        }
    }
}
