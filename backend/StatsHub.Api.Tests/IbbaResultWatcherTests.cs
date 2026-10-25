using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StatsHub.Api.Models;
using StatsHub.Api.Services;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

// A followed team's result is picked up soon after its game should be over,
// not at the next nightly sync.
public class IbbaResultWatcherTests : IClassFixture<IbbaResultWatcherTests.Factory>
{
    public sealed class Factory : StatsHubFactory
    {
        public static ByteArrayContent? Export { get; set; }
        public Factory() { IbbaSite = () => new Site(); }

        // A team page with its spreadsheet link, and the spreadsheet itself.
        private sealed class Site : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var uri = request.RequestUri!;
                HttpContent content = uri.Query.Contains("feed=xlsx")
                    ? new ByteArrayContent(Export!.ReadAsByteArrayAsync().Result)
                    : new StringContent("<html><h1>Maccabi</h1><a href='?feed=xlsx&team_id=77'>יצוא לאקסל</a></html>");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
            }
        }
    }

    private readonly Factory _factory;
    public IbbaResultWatcherTests(Factory factory) => _factory = factory;

    private async Task<(IbbaTeam Own, IbbaTeam Opponent)> SeedAsync(bool followed)
    {
        IbbaTeam own = null!, opponent = null!;
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        await _factory.WithDbAsync(async db =>
        {
            own = new IbbaTeam { IbbaTeamId = $"{Random.Shared.Next(100000, 999999)}", TeamUrl = $"https://ibasketball.co.il/team/{Guid.NewGuid():N}/", Name = "Maccabi" };
            opponent = new IbbaTeam { IbbaTeamId = $"{Random.Shared.Next(100000, 999999)}", TeamUrl = $"https://ibasketball.co.il/team/{Guid.NewGuid():N}/", Name = "Hapoel" };
            db.IbbaTeams.AddRange(own, opponent);
            if (followed)
            {
                var userId = (await db.Players.FindAsync(player.Id))!.UserId;
                var season = await db.Seasons.FirstOrDefaultAsync(s => s.UserId == userId)
                    ?? new Season { UserId = userId, Name = "2026-27", Year = 2026, StartDate = DateTime.UtcNow };
                var team = new Team { Name = "Maccabi", Season = season, IbbaTeamId = own.Id };
                db.Teams.Add(team);
                db.PlayerTeams.Add(new PlayerTeam { PlayerId = player.Id, Team = team });
            }
            await db.SaveChangesAsync();
        });
        return (own, opponent);
    }

    private async Task<Game> AddGameAsync(IbbaTeam own, IbbaTeam opponent, DateTime when, string status = "Upcoming")
    {
        var game = new Game { IbbaGameCode = $"g{Guid.NewGuid():N}", HomeTeamId = own.Id, AwayTeamId = opponent.Id, GameDate = when, Status = status };
        await _factory.WithDbAsync(async db => { db.Games.Add(game); await db.SaveChangesAsync(); });
        return game;
    }

    private async Task<T> WithServiceAsync<T>(Func<IIbbaService, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IIbbaService>());
    }

    [Fact]
    public async Task Only_followed_teams_with_a_game_that_should_be_over_are_checked()
    {
        var now = DateTime.UtcNow;
        var (due, dueOpponent) = await SeedAsync(followed: true);
        await AddGameAsync(due, dueOpponent, now.AddHours(-2));                                   // over, no result -> check
        var (stillPlaying, o1) = await SeedAsync(followed: true);
        await AddGameAsync(stillPlaying, o1, now.AddMinutes(-30));                                // still being played
        var (tooOld, o2) = await SeedAsync(followed: true);
        await AddGameAsync(tooOld, o2, now.AddHours(-8));                                         // left for the nightly sync
        var (hasResult, o3) = await SeedAsync(followed: true);
        await AddGameAsync(hasResult, o3, now.AddHours(-2), status: "Completed");
        var (notFollowed, o4) = await SeedAsync(followed: false);
        await AddGameAsync(notFollowed, o4, now.AddHours(-2));

        var checkedTeams = await WithServiceAsync(s => s.TeamsAwaitingResultsAsync(now));

        Assert.Contains(due.Id, checkedTeams);
        Assert.DoesNotContain(stillPlaying.Id, checkedTeams);
        Assert.DoesNotContain(tooOld.Id, checkedTeams);
        Assert.DoesNotContain(hasResult.Id, checkedTeams);
        Assert.DoesNotContain(notFollowed.Id, checkedTeams);
        Assert.DoesNotContain(dueOpponent.Id, checkedTeams); // nobody follows the opponent
    }

    [Fact]
    public async Task The_result_is_read_from_the_teams_spreadsheet()
    {
        var (own, opponent) = await SeedAsync(followed: true);
        var israel = TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Jerusalem", out var tz) ? tz : TimeZoneInfo.FindSystemTimeZoneById("Israel Standard Time");
        var tipOff = DateTime.UtcNow.AddHours(-2);
        var local = TimeZoneInfo.ConvertTimeFromUtc(tipOff, israel);
        var game = await AddGameAsync(own, opponent, TimeZoneInfo.ConvertTimeToUtc(new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0), israel));
        Factory.Export = IbbaLayoutTests.Sheet(
            ["ליגה", "Code", "Week Day", "תאריך", "מחזור", "Time", "Home Team", "Home Team Code", "Away Team", "Away Team Code", "Venue", "Home Score", "Away Score"],
            ["נערים א", game.IbbaGameCode!, "Sun", local.ToString("dd-MM-yyyy"), "3", local.ToString("HH:mm"), "Maccabi", own.IbbaTeamId, "Hapoel", opponent.IbbaTeamId, "Gym", "70", "64"]);

        await WithServiceAsync(async s => { await s.RefreshTeamGamesAsync(own.Id); return true; });

        Game saved = null!;
        await _factory.WithDbAsync(async db => saved = await db.Games.SingleAsync(g => g.Id == game.Id));
        Assert.Equal(("Completed", 70, 64), (saved.Status, saved.HomeScore, saved.AwayScore));
    }
}
