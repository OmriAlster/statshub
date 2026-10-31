using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StatsHub.Api.Data;
using StatsHub.Api.Models;
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

    [Fact]
    public async Task A_team_IBBA_renamed_stays_one_team_at_its_new_address()
    {
        // IBBA keeps a team's id when it renames it, but its address changes.
        var code = Random.Shared.Next(900000, 999999).ToString();
        var league = $"https://ibasketball.co.il/league/{Guid.NewGuid():N}/";
        await SyncAndReadAsync(league, Row(1, "הפועל גלבוע מעיינות", $"https://ibasketball.co.il/team/{code}-הפועל-גלבוע-מעיינות/"));

        var table = await SyncAndReadAsync(league, Row(1, "הפועל העמק", $"https://ibasketball.co.il/team/{code}-הפועל-העמק/"));

        Assert.Equal(new[] { "1 הפועל העמק" }, table);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teams = await db.IbbaTeams.Where(t => t.IbbaTeamId == code).ToListAsync();
        Assert.Equal($"https://ibasketball.co.il/team/{code}-הפועל-העמק/", Assert.Single(teams).TeamUrl);
    }

    [Fact]
    public async Task Two_saved_teams_with_one_id_dont_stop_a_teams_games_from_saving()
    {
        // Left over from before renamed teams were recognised.
        var (own, rival) = (Random.Shared.Next(800000, 849999).ToString(), Random.Shared.Next(850000, 899999).ToString());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownTeam = new IbbaTeam { IbbaTeamId = own, TeamUrl = $"https://ibasketball.co.il/team/{own}-own/", Name = "Own" };
        var oldName = new IbbaTeam { IbbaTeamId = rival, TeamUrl = $"https://ibasketball.co.il/team/{rival}-old/", Name = "Old name", SyncedAt = DateTime.UtcNow.AddYears(-1) };
        var newName = new IbbaTeam { IbbaTeamId = rival, TeamUrl = $"https://ibasketball.co.il/team/{rival}-new/", Name = "New name", SyncedAt = DateTime.UtcNow };
        db.IbbaTeams.AddRange(ownTeam, oldName, newName);
        await db.SaveChangesAsync();
        var service = (IbbaService)scope.ServiceProvider.GetRequiredService<IIbbaService>();

        var gameCode = Random.Shared.Next(100000000, 999999999).ToString();
        await service.UpsertGamesAsync(ownTeam, [new LeagueGame { League = "L", Code = gameCode, Date = "1-11-2026", Time = "18:00", HomeTeam = "Own", HomeTeamCode = own, AwayTeam = "New name", AwayTeamCode = rival, Venue = "Gym" }]);

        var game = await db.Games.AsNoTracking().SingleAsync(g => g.IbbaGameCode == gameCode);
        Assert.Equal(newName.Id, game.AwayTeamId);
    }
}
