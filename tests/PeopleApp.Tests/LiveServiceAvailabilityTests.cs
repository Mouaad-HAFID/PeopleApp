using PeopleApp.Api;

namespace PeopleApp.Tests;

[Trait("Category", "LiveAvailability")]
public sealed class LiveServiceAvailabilityTests
{
    [Fact]
    public async Task TripPinServiceProvidesUsablePeopleCollection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        var session = await PeopleServiceSession.OpenDefaultAsync(client, timeout.Token);
        var people = await session.GetPeopleAsync(timeout.Token);

        Assert.Contains(
            "/TripPinRESTierService/(S(",
            session.SessionRoot.AbsolutePath,
            StringComparison.Ordinal);
        Assert.All(people, person => Assert.False(string.IsNullOrWhiteSpace(person.UserName)));
    }
}
