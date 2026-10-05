using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StatsHub.Api.IbbaScraping;
using StatsHub.Api.Models;
using StatsHub.Api.Services;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

// A game IBBA removed from a team's schedule before it was played was
// cancelled - the sync deletes it from the app too. Anything already played,
// tracked or scored stays.
public class IbbaGameSyncTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public IbbaGameSyncTests(StatsHubFactory factory) => _factory = factory;

    private async Task<(IbbaTeam Own, IbbaTeam Opponent)> SeedTeamsAsync()
    {
        IbbaTeam own = null!, opponent = null!;
        await _factory.WithDbAsync(async db =>
        {
            own = new IbbaTeam { IbbaTeamId = $"t{Guid.NewGuid():N}", TeamUrl = $"https://example.test/{Guid.NewGuid():N}", Name = "Maccabi" };
            opponent = new IbbaTeam { IbbaTeamId = $"t{Guid.NewGuid():N}", TeamUrl = $"https://example.test/{Guid.NewGuid():N}", Name = "Hapoel" };
            db.IbbaTeams.AddRange(own, opponent);
            await db.SaveChangesAsync();
        });
        return (own, opponent);
    }

    private static IbbaGameRow Row(string code, IbbaTeam own, IbbaTeam opponent, DateTime israelDate, int? homeScore = null, int? awayScore = null) => new()
    {
        League = "U16 North",
        Code = code,
        Date = israelDate.ToString("dd-MM-yyyy"),
        Time = "18:00",
        HomeTeam = own.Name,
        HomeTeamCode = own.IbbaTeamId,
        AwayTeam = opponent.Name,
        AwayTeamCode = opponent.IbbaTeamId,
        Venue = "Gym",
        HomeScore = homeScore,
        AwayScore = awayScore,
    };

    private async Task SyncAsync(IbbaTeam own, params IbbaGameRow[] rows)
    {
        using var scope = _factory.Services.CreateScope();
        var service = (IbbaService)scope.ServiceProvider.GetRequiredService<IIbbaService>();
        await service.UpsertGamesAsync(own, rows.ToList());
    }

    private async Task<List<string>> GameCodesAsync(IbbaTeam own)
    {
        var codes = new List<string>();
        await _factory.WithDbAsync(async db => codes.AddRange(await db.Games
            .Where(g => g.HomeTeamId == own.Id || g.AwayTeamId == own.Id)
            .Select(g => g.IbbaGameCode!)
            .ToListAsync()));
        return codes;
    }

    private static string Code() => $"g{Guid.NewGuid():N}";

    [Fact]
    public async Task An_upcoming_game_removed_from_ibba_is_deleted()
    {
        var (own, opponent) = await SeedTeamsAsync();
        var (kept, cancelled) = (Code(), Code());
        var nextWeek = DateTime.UtcNow.AddDays(7);
        await SyncAsync(own, Row(kept, own, opponent, nextWeek), Row(cancelled, own, opponent, nextWeek.AddDays(7)));

        await SyncAsync(own, Row(kept, own, opponent, nextWeek));

        Assert.Equal(new[] { kept }, await GameCodesAsync(own));
    }

    [Fact]
    public async Task Played_and_past_games_stay_when_they_leave_the_export()
    {
        var (own, opponent) = await SeedTeamsAsync();
        var (played, pastUnplayed, other) = (Code(), Code(), Code());
        await SyncAsync(own,
            Row(played, own, opponent, DateTime.UtcNow.AddDays(-7), 70, 60),
            Row(pastUnplayed, own, opponent, DateTime.UtcNow.AddDays(-3)),
            Row(other, own, opponent, DateTime.UtcNow.AddDays(7)));

        await SyncAsync(own, Row(other, own, opponent, DateTime.UtcNow.AddDays(7)));

        Assert.Equivalent(new[] { played, pastUnplayed, other }, (await GameCodesAsync(own)).ToArray(), strict: true);
    }

    [Fact]
    public async Task An_upcoming_game_with_stats_stays_when_it_leaves_the_export()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var (own, opponent) = await SeedTeamsAsync();
        var (tracked, other) = (Code(), Code());
        await SyncAsync(own, Row(tracked, own, opponent, DateTime.UtcNow.AddDays(7)), Row(other, own, opponent, DateTime.UtcNow.AddDays(14)));
        await _factory.WithDbAsync(async db =>
        {
            var game = await db.Games.SingleAsync(g => g.IbbaGameCode == tracked);
            db.GameStats.Add(new GameStats { GameId = game.Id, PlayerId = player.Id });
            await db.SaveChangesAsync();
        });

        await SyncAsync(own, Row(other, own, opponent, DateTime.UtcNow.AddDays(14)));

        Assert.Equivalent(new[] { tracked, other }, (await GameCodesAsync(own)).ToArray(), strict: true);
    }

    [Fact]
    public async Task An_empty_export_deletes_nothing()
    {
        var (own, opponent) = await SeedTeamsAsync();
        var upcoming = Code();
        await SyncAsync(own, Row(upcoming, own, opponent, DateTime.UtcNow.AddDays(7)));

        await SyncAsync(own);

        Assert.Equal(new[] { upcoming }, await GameCodesAsync(own));
    }
}
