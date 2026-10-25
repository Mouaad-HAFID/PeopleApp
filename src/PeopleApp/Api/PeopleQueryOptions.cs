namespace PeopleApp.Api;

public enum PersonFilterField
{
    UserName,
    FirstName,
    LastName,
    Gender
}

public enum FilterCombination
{
    And,
    Or
}

public enum PersonSortField
{
    UserName,
    FirstName,
    LastName,
    Gender,
    Age
}

public enum SortDirection
{
    Ascending,
    Descending
}

public sealed record PersonFilter(PersonFilterField Field, string Value);

public sealed class PeopleQueryOptions
{
    public PeopleQueryOptions(
        IEnumerable<PersonFilter>? filters = null,
        FilterCombination combination = FilterCombination.And,
        int? top = null,
        int? skip = null,
        PersonSortField? sortField = null,
        SortDirection sortDirection = SortDirection.Ascending)
    {
        Filters = filters?.ToArray() ?? [];
        Combination = combination;
        Top = top;
        Skip = skip;
        SortField = sortField;
        SortDirection = sortDirection;
    }

    public IReadOnlyList<PersonFilter> Filters { get; }

    public FilterCombination Combination { get; }

    public int? Top { get; }

    public int? Skip { get; }

    public PersonSortField? SortField { get; }

    public SortDirection SortDirection { get; }
}
