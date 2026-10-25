using System.Globalization;

namespace PeopleApp.Api;

internal static class PeopleQueryBuilder
{
    public static Uri BuildCollectionUri(Uri sessionRoot, PeopleQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(sessionRoot);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var queryParameters = new List<string>();
        if (options.Filters.Count > 0)
        {
            var separator = options.Combination == FilterCombination.And ? " and " : " or ";
            var filterExpression = string.Join(separator, options.Filters.Select(BuildFilter));
            queryParameters.Add(Encode("$filter", filterExpression));
        }

        if (options.Top is not null)
        {
            queryParameters.Add($"$top={options.Top.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (options.Skip is not null)
        {
            queryParameters.Add($"$skip={options.Skip.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (options.SortField is not null)
        {
            var direction = options.SortDirection == SortDirection.Ascending ? "asc" : "desc";
            var orderBy = $"{options.SortField.Value} {direction}";
            queryParameters.Add(Encode("$orderby", orderBy));
        }

        var collectionUri = new Uri(sessionRoot, "People");
        return queryParameters.Count == 0
            ? collectionUri
            : new Uri($"{collectionUri.AbsoluteUri}?{string.Join("&", queryParameters)}", UriKind.Absolute);
    }

    private static string BuildFilter(PersonFilter filter)
    {
        var literal = $"'{filter.Value.Replace("'", "''", StringComparison.Ordinal)}'";
        return filter.Field switch
        {
            PersonFilterField.UserName => $"UserName eq {literal}",
            PersonFilterField.FirstName => $"contains(FirstName,{literal})",
            PersonFilterField.LastName => $"contains(LastName,{literal})",
            PersonFilterField.Gender => $"Gender eq Trippin.PersonGender{literal}",
            _ => throw new ArgumentOutOfRangeException(nameof(filter.Field))
        };
    }

    private static string Encode(string name, string value) =>
        $"{name}={Uri.EscapeDataString(value)}";

    private static void Validate(PeopleQueryOptions options)
    {
        if (options.Top < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Top), "Top must be zero or greater.");
        }

        if (options.Skip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Skip), "Skip must be zero or greater.");
        }

        if (!Enum.IsDefined(options.Combination))
        {
            throw new ArgumentOutOfRangeException(nameof(options.Combination));
        }

        if (!Enum.IsDefined(options.SortDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(options.SortDirection));
        }

        if (options.SortField is not null && !Enum.IsDefined(options.SortField.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(options.SortField));
        }

        foreach (var filter in options.Filters)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentNullException.ThrowIfNull(filter.Value);

            if (!Enum.IsDefined(filter.Field))
            {
                throw new ArgumentOutOfRangeException(nameof(filter.Field));
            }

            if (filter.Field == PersonFilterField.Gender &&
                (!Enum.TryParse<PersonGender>(filter.Value, ignoreCase: false, out var gender) ||
                    !Enum.IsDefined(gender) ||
                    gender.ToString() != filter.Value))
            {
                throw new ArgumentException("Gender must be Male, Female, or Unknown.", nameof(options));
            }
        }
    }

    private enum PersonGender
    {
        Male,
        Female,
        Unknown
    }
}
