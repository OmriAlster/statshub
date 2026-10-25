using Microsoft.Extensions.DependencyInjection;
using StatsHub.Api.IbbaScraping;
using StatsHub.Api.Services;
using StatsHub.Api.Tests.Infrastructure;
using StatsHub.Api.Leagues;

namespace StatsHub.Api.Tests;

// The league table shows exactly the teams in IBBA's current table - a team
// that withdrew leaves it on the next sync.
public class IbbaStandingsTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public IbbaStandingsTests(StatsHubFactory factory) => _factory = factory;

    private static LeagueStanding Row(int position, string name, string url) =>
        new() { Position = position, TeamName = name, TeamUrl = url, GamesPlayed = 5, Wins = 6 - position, Losses = position - 1 };

    private async Task<List<string>> SyncAndReadAsync(string leagueUrl, params LeagueStanding[] rows)
    {
        using var scope = _factory.Services.CreateScope();
        var service = (IbbaService)scope.ServiceProvider.GetRequiredService<IIbbaService>();
        await service.UpsertStandingsAsync(leagueUrl, "U16 North", rows.ToList());
        return (await service.GetStandingsAsync(leagueUrl)).Select(s => $"{s.Position} {s.TeamName}").ToList();
    }

    [Fact]
    public async Task A_team_that_withdrew_leaves_the_league_table()
    {
        var league = $"https://example.test/league/{Guid.NewGuid():N}";
        var (a, b, c) = ($"https://example.test/team/1{Guid.NewGuid():N}/", $"https://example.test/team/2{Guid.NewGuid():N}/", $"https://example.test/team/3{Guid.NewGuid():N}/");
        await SyncAndReadAsync(league, Row(1, "Maccabi", a), Row(2, "Hapoel", b), Row(3, "Elitzur", c));

        var table = await SyncAndReadAsync(league, Row(1, "Maccabi", a), Row(2, "Elitzur", c));

        Assert.Equal(new[] { "1 Maccabi", "2 Elitzur" }, table);
    }

    [Fact]
    public async Task A_team_that_comes_back_returns_to_the_table()
    {
        var league = $"https://example.test/league/{Guid.NewGuid():N}";
        var (a, b) = ($"https://example.test/team/1{Guid.NewGuid():N}/", $"https://example.test/team/2{Guid.NewGuid():N}/");
        await SyncAndReadAsync(league, Row(1, "Maccabi", a), Row(2, "Hapoel", b));
        await SyncAndReadAsync(league, Row(1, "Maccabi", a));

        var table = await SyncAndReadAsync(league, Row(1, "Maccabi", a), Row(2, "Hapoel", b));

        Assert.Equal(new[] { "1 Maccabi", "2 Hapoel" }, table);
    }
}
