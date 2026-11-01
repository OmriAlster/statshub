using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;
using StatsHub.Api.Services;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

// A linked player's line read off the game's scoresheet (Swish): it becomes the
// game's stats only when the parent recorded none, and its number becomes the
// player's on that team unless the parent set one.
public class ScoresheetStatsTests : IClassFixture<ScoresheetStatsTests.Factory>
{
    // The youth fixture: בית"ר כפר יונה 70 - מכבי תל מונד 54. On Tel Mond, "כאהן, א." is
    // #16 (code 188): 18 points - 5 free throws, five 2s, one 3 - and a foul.
    private const string League = "נוער ארצית שרון";
    private const string Home = "בית\"ר כפר יונה";
    private const string Away = "מכבי תל מונד";
    private static readonly DateTime TipOff = DateTime.UtcNow.AddDays(-3);

    public sealed class Factory : StatsHubFactory
    {
        public Factory() { SwishSite = () => new Swish(); }

        private sealed class Swish : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var uri = request.RequestUri!;
                HttpContent content = uri.AbsolutePath switch
                {
                    "/competitions" => Json($$"""[{"id":"c1","name":"{{League}}","groups":[{"id":"g1","name":"A"}]}]"""),
                    "/calendar" => Json($$$"""
                        {"id":"g1","rounds":[{"name":"1","matches":[{"id":"m1","homeTeam":{"id":"h","name":"{{{Home.Replace("\"", "\\\"")}}}"},
                         "awayTeam":{"id":"a","name":"{{{Away}}}"},"date":"{{{TipOff:yyyy-MM-ddTHH:mm:ssZ}}}","status":"CLOSED",
                         "score":{"totals":[{"teamId":"h","total":70},{"teamId":"a","total":54}]}}]}]}
                        """),
                    "/match" => Json("""{"id":"m1","scoresheetUrl":"https://sheets.test/m1.pdf"}"""),
                    _ => new ByteArrayContent(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Scoresheets", "Fixtures", "ibba-youth-2026-10-19-kfar-yona-tel-mond.pdf"))),
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
            }

            private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
        }
    }

    private readonly Factory _factory;
    public ScoresheetStatsTests(Factory factory) => _factory = factory;

    private sealed record Seeded(TestUser Parent, Guid PlayerId, Guid TeamId, Guid LinkId, Guid GameId);

    // A parent's player "אורי כאהן", linked to Tel Mond, with the finished game in the app.
    private async Task<Seeded> SeedAsync(int? jersey = null)
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.PostAsync<PlayerDto>("/api/players", new CreatePlayerDto { FirstName = "אורי", LastName = "כאהן", Position = "" });
        var team = await parent.CreateTeamWithPlayerAsync(player.Id, Away, jersey);
        Guid linkId = default, gameId = default;
        await _factory.WithDbAsync(async db =>
        {
            var own = new IbbaTeam { IbbaTeamId = $"{Random.Shared.Next(100000, 999999)}", TeamUrl = $"https://ibasketball.co.il/team/{Guid.NewGuid():N}/", Name = Away, LeagueName = League };
            var opponent = new IbbaTeam { IbbaTeamId = $"{Random.Shared.Next(100000, 999999)}", TeamUrl = $"https://ibasketball.co.il/team/{Guid.NewGuid():N}/", Name = Home, LeagueName = League };
            db.IbbaTeams.AddRange(own, opponent);
            (await db.Teams.FindAsync(team.Id))!.IbbaTeamId = own.Id;
            var link = new PlayerIbbaLink { PlayerId = player.Id, IbbaPlayerUrl = $"https://ibasketball.co.il/player/{Guid.NewGuid():N}/" };
            db.PlayerIbbaLinks.Add(link);
            db.PlayerIbbaTeams.Add(new PlayerIbbaTeam { PlayerIbbaLink = link, IbbaTeamId = own.Id });
            var game = new Game { IbbaGameCode = $"g{Guid.NewGuid():N}", Provider = "ibba", HomeTeamId = opponent.Id, AwayTeamId = own.Id, GameDate = TipOff, Status = "Completed", HomeScore = 70, AwayScore = 54 };
            db.Games.Add(game);
            await db.SaveChangesAsync();
            (linkId, gameId) = (link.Id, game.Id);
        });
        return new Seeded(parent, player.Id, team.Id, linkId, gameId);
    }

    private async Task FillAsync(Guid linkId)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IScoresheetStatsService>().FillAsync(linkId);
    }

    [Fact]
    public async Task With_no_stats_recorded_the_sheet_becomes_the_games_stats_and_gives_the_number()
    {
        var s = await SeedAsync();

        await FillAsync(s.LinkId);

        var game = await s.Parent.GetAsync<GameDto>($"/api/games/{s.GameId}");
        var line = Assert.Single(game.PlayerStats);
        Assert.Equal(GameStats.ScoresheetSource, line.Source);
        Assert.Equal((18, 5, 5, 1, 1), (line.TotalPoints, line.FreeThrowsMade, line.FieldGoalsMade, line.ThreePointersMade, line.Fouls));
        // What a sheet doesn't have stays unknown, not 0.
        Assert.Null(line.FieldGoalsAttempted);
        Assert.Null(line.FreeThrowPercentage);
        Assert.Null(line.TotalRebounds);
        Assert.Null(line.MinutesPlayed);
        Assert.Equal(16, line.Scoresheet!.Number);
        Assert.True(line.Scoresheet.SheetValid);

        var stats = Assert.Single(await s.Parent.GetAsync<List<PlayerTeamStatsDto>>($"/api/gamestats/player/{s.PlayerId}"));
        Assert.Equal(16, stats.JerseyNumber);
        await _factory.WithDbAsync(async db => Assert.Equal("188", (await db.PlayerIbbaLinks.FindAsync(s.LinkId))!.ScoresheetCode));
    }

    [Fact]
    public async Task The_parents_own_stats_and_number_win()
    {
        var s = await SeedAsync(jersey: 7);
        var mine = await s.Parent.CreateStatsAsync(s.GameId, s.PlayerId, ftm: 2, fta: 4, reb: 6, minutes: 25);

        await FillAsync(s.LinkId);

        var game = await s.Parent.GetAsync<GameDto>($"/api/games/{s.GameId}");
        var line = Assert.Single(game.PlayerStats);
        Assert.Equal(mine.Id, line.Id);
        Assert.Null(line.Source);
        Assert.Equal((2, 4, 6, 25), (line.FreeThrowsMade, line.FreeThrowsAttempted, line.TotalRebounds, line.MinutesPlayed));
        // ...with the sheet's version next to it.
        Assert.Equal(18, line.Scoresheet!.Points);

        var stats = Assert.Single(await s.Parent.GetAsync<List<PlayerTeamStatsDto>>($"/api/gamestats/player/{s.PlayerId}"));
        Assert.Equal(7, stats.JerseyNumber);
    }

    [Fact]
    public async Task Once_the_parent_edits_a_sheets_stats_they_are_theirs()
    {
        var s = await SeedAsync();
        await FillAsync(s.LinkId);
        var game = await s.Parent.GetAsync<GameDto>($"/api/games/{s.GameId}");

        var edited = await s.Parent.PutAsync<GameStatsDto>($"/api/gamestats/{game.PlayerStats[0].Id}", new UpdateGameStatsDto { MinutesPlayed = 22 });

        Assert.Null(edited.Source);
        Assert.Equal((22, 18), (edited.MinutesPlayed, edited.TotalPoints));
    }

    [Fact]
    public async Task Stats_a_sheet_lacks_are_averaged_over_the_games_that_have_them()
    {
        var s = await SeedAsync();
        await FillAsync(s.LinkId); // 18 points, nothing else known
        var other = await s.Parent.CreateGameAsync(s.TeamId, opponent: "Rivals");
        await s.Parent.CreateStatsAsync(other.Id, s.PlayerId, ftm: 2, fta: 4, reb: 6, minutes: 30); // 2 points

        var stats = Assert.Single(await s.Parent.GetAsync<List<PlayerTeamStatsDto>>($"/api/gamestats/player/{s.PlayerId}"));

        Assert.Equal(2, stats.GamesPlayed);
        Assert.Equal(10, stats.PointsPerGame);         // both games
        Assert.Equal(6, stats.ReboundsPerGame);        // the parent's game only
        Assert.Equal(50, stats.FreeThrowPercentage);   // 2 of 4 - not the sheet's 5 with no attempts
        Assert.Equal(30, stats.TotalMinutes);
        Assert.Equal(0, stats.FieldGoalPercentage);    // the parent recorded 0 attempts - known, unlike the sheet
    }

    [Fact]
    public async Task A_finished_game_without_its_sheet_is_watched()
    {
        var s = await SeedAsync();
        await _factory.WithDbAsync(async db =>
        {
            var game = await db.Games.FindAsync(s.GameId);
            game!.GameDate = DateTime.UtcNow.AddHours(-3);
            await db.SaveChangesAsync();
        });

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IScoresheetStatsService>();
        Assert.Contains(s.LinkId, await service.LinksAwaitingScoresheetsAsync(DateTime.UtcNow));

        await _factory.WithDbAsync(async db =>
        {
            var game = await db.Games.FindAsync(s.GameId);
            game!.GameDate = TipOff; // and once it has its line, no more
            await db.SaveChangesAsync();
        });
        await FillAsync(s.LinkId);
        await _factory.WithDbAsync(async db =>
        {
            var game = await db.Games.FindAsync(s.GameId);
            game!.GameDate = DateTime.UtcNow.AddHours(-3);
            await db.SaveChangesAsync();
        });
        Assert.DoesNotContain(s.LinkId, await service.LinksAwaitingScoresheetsAsync(DateTime.UtcNow));
    }
}
