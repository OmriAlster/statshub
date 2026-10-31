using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StatsHub.Api.DTOs;
using StatsHub.Api.Leagues;
using StatsHub.Api.Models;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

// A second league plugs in with nothing but its provider: a player linked
// from its site gets their teams, games and table - and its ids never mix
// with IBBA's, even where they're the same numbers.
public class SecondLeagueTests : IClassFixture<SecondLeagueTests.Factory>
{
    public sealed class Factory : StatsHubFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton<ILeagueProvider, DemoLeague>());
        }
    }

    // A league whose team and game ids deliberately repeat IBBA's ("151", "783912").
    private sealed class DemoLeague : ILeagueProvider
    {
        public const string TeamUrl = "https://demo-league.test/teams/151";
        public const string LeagueUrl = "https://demo-league.test/leagues/u16";

        public string Key => "demo";
        public string Name => "Demo League";
        public string LogoUrl => "/icons/demo.png";
        public string PlayerUrlExample => "https://demo-league.test/players/…";
        public bool IsPlayerUrl(string url) => url.StartsWith("https://demo-league.test/players/");

        public Task<LeaguePlayer> GetPlayerAsync(string playerUrl) => Task.FromResult(new LeaguePlayer
        {
            PlayerName = "Noa Cohen",
            PlayerUrl = playerUrl,
            DateOfBirth = new DateTime(2011, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Teams = [new LeagueTeam { TeamName = "Demo Lions", TeamUrl = TeamUrl, TeamSlugId = "151" }],
        });

        public async Task<(LeaguePlayer Player, List<LeagueTeamPage> Teams)> LoadTeamsAsync(string playerUrl, LeaguePlayer? prefetchedPlayer = null)
        {
            var player = prefetchedPlayer ?? await GetPlayerAsync(playerUrl);
            return (player, player.Teams.Select(t => new LeagueTeamPage { Team = t }).ToList());
        }

        public Task<LeagueTeamReport> LoadGamesAndStandingsAsync(LeagueTeamPage page)
        {
            page.Team.LeagueName = "Demo U16";
            page.Team.LeagueUrl = LeagueUrl;
            return Task.FromResult(new LeagueTeamReport
            {
                Team = page.Team,
                Games = Games(),
                Standings =
                [
                    new LeagueStanding { Position = 1, TeamName = "Demo Lions", TeamUrl = TeamUrl, GamesPlayed = 1, Wins = 1, LeaguePoints = 2 },
                    new LeagueStanding { Position = 2, TeamName = "Demo Bears", TeamUrl = "https://demo-league.test/teams/152", GamesPlayed = 1, Losses = 1, LeaguePoints = 1 },
                ],
            });
        }

        private static List<LeagueGame> Games() =>
        [
            new LeagueGame { League = "Demo U16", Code = "783912", Date = "4-10-2026", Time = "18:00", HomeTeam = "Demo Lions", HomeTeamCode = "151", AwayTeam = "Demo Bears", AwayTeamCode = "152", Venue = "Demo Arena", HomeScore = 70, AwayScore = 60 },
            new LeagueGame { League = "Demo U16", Code = "800001", Date = "1-12-2026", Time = "18:00", HomeTeam = "Demo Bears", HomeTeamCode = "152", AwayTeam = "Demo Lions", AwayTeamCode = "151", Venue = "Bear Den" },
        ];

        public Task<List<LeagueGame>> LoadTeamGamesAsync(string teamUrl) => Task.FromResult(Games());
        public Task<(string CanonicalUrl, string Name, string? LogoUrl)?> ResolveTeamByIdAsync(string teamId, string? teamName = null) => Task.FromResult<(string, string, string?)?>(null);
        public Task<string?> FindTeamLogoUrlAsync(string teamUrl, string teamName) => Task.FromResult<string?>(null);
        public string TeamIdFromUrl(string teamUrl) => teamUrl.Split('/').Last();

        public Task<List<LeaguePlayerHit>> SearchPlayersAsync(string name) => Task.FromResult<List<LeaguePlayerHit>>(
            "Noa Cohen".Contains(name, StringComparison.OrdinalIgnoreCase) ? [new("Noa Cohen", "https://demo-league.test/players/77")] : []);
    }

    private readonly Factory _factory;
    public SecondLeagueTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Both_leagues_are_offered()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var leagues = await parent.GetAsync<List<LeagueInfo>>("/api/leagues");

        Assert.Equivalent(new[] { "IBBA", "Demo League" }, leagues.Select(l => l.Name).ToArray());
    }

    private record LeagueInfo(string Key, string Name, string LogoUrl, string PlayerUrlExample);

    [Fact]
    public async Task A_player_from_the_second_league_gets_its_teams_games_and_table_apart_from_IBBA()
    {
        // An IBBA team and game with the very same ids as the demo league's.
        var ibbaTeam = new IbbaTeam { IbbaTeamId = "151", TeamUrl = $"https://ibasketball.co.il/team/151-{Guid.NewGuid():N}/", Name = "IBBA Maccabi" };
        var ibbaGame = new Game { IbbaGameCode = "783912", Provider = "ibba", GameDate = DateTime.UtcNow.AddDays(-5), Status = "Upcoming", GameType = "League", Location = "IBBA Gym" };
        await _factory.WithDbAsync(async db =>
        {
            db.IbbaTeams.Add(ibbaTeam);
            ibbaGame.HomeTeamId = ibbaTeam.Id;
            db.Games.Add(ibbaGame);
            await db.SaveChangesAsync();
        });

        var parent = await TestUser.SignInAsync(_factory);
        var created = await parent.PostAsync<CreatePlayerFromIbbaResultDto>("/api/ibba/players", new { ibbaPlayerUrl = "https://demo-league.test/players/7" });
        Assert.Equal("Demo League", created.Ibba!.LeagueName);

        // The games load in the background.
        IbbaLinkStatusDto status = created.Ibba;
        // Up to 30 s - a busy CI machine can take several seconds.
        for (var i = 0; i < 300 && status.GamesLoading; i++)
        {
            await Task.Delay(100);
            status = await parent.GetAsync<IbbaLinkStatusDto>($"/api/players/{created.Player.Id}/ibba");
        }
        Assert.False(status.GamesLoading, "games still loading after 30 s");
        Assert.Null(status.LastSyncError);
        var team = status.Teams.Single();
        Assert.Equal(("Demo Lions", 1, 2), (team.TeamName, team.Position, team.TotalTeams));

        var games = await parent.GetAsync<List<GameDto>>($"/api/games/player/{created.Player.Id}");
        Assert.Equivalent(new[] { ("Demo Bears", "Completed", 70), ("Demo Bears", "Upcoming", 0) },
            games.Select(g => (g.OpponentName, g.Status, g.TeamScore ?? 0)).ToArray());
        Assert.All(games, g => Assert.Equal("demo", g.League));

        await _factory.WithDbAsync(async db =>
        {
            // IBBA's team and game with the same ids are untouched and separate.
            var untouched = await db.Games.SingleAsync(g => g.Id == ibbaGame.Id);
            Assert.Equal(("Upcoming", "IBBA Gym", ibbaTeam.Id), (untouched.Status, untouched.Location, untouched.HomeTeamId!.Value));
            Assert.Equal(2, await db.Games.CountAsync(g => g.IbbaGameCode == "783912"));
            var demoTeams = await db.IbbaTeams.Where(t => t.Provider == "demo").Select(t => t.IbbaTeamId).ToListAsync();
            Assert.Equivalent(new[] { "151", "152" }, demoTeams.ToArray());
            Assert.Equal("ibba", (await db.IbbaTeams.SingleAsync(t => t.Id == ibbaTeam.Id)).Provider);
        });
    }

    [Fact]
    public async Task A_page_from_an_unsupported_site_is_refused_with_the_leagues_that_are()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var response = await parent.Http.PostAsJsonAsync("/api/ibba/players", new { ibbaPlayerUrl = "https://example.com/players/1" });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("IBBA", body);
        Assert.Contains("Demo League", body);
    }

    [Fact]
    public async Task A_league_player_already_on_your_players_page_is_flagged_and_not_added_twice()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var created = await parent.PostAsync<CreatePlayerFromIbbaResultDto>("/api/ibba/players", new { ibbaPlayerUrl = "https://demo-league.test/players/42" });

        // The same page, pasted a little differently.
        var again = "https://demo-league.test/players/42/#";
        var preview = await parent.GetAsync<IbbaPreviewDto>($"/api/ibba/preview?playerUrl={Uri.EscapeDataString(again)}");
        Assert.Equal(created.Player.Id, preview.AlreadyLinkedTo?.PlayerId);

        var add = await parent.Http.PostAsJsonAsync("/api/ibba/players", new { ibbaPlayerUrl = again });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, add.StatusCode);
        Assert.Contains("already connected", await add.Content.ReadAsStringAsync());

        // Another family can still add the same player.
        var other = await TestUser.SignInAsync(_factory);
        var theirs = await other.GetAsync<IbbaPreviewDto>($"/api/ibba/preview?playerUrl={Uri.EscapeDataString(again)}");
        Assert.Null(theirs.AlreadyLinkedTo);
    }

    [Fact]
    public async Task A_player_can_be_found_by_name_and_shows_once_added()
    {
        var parent = await TestUser.SignInAsync(_factory);

        var hit = Assert.Single(await parent.GetAsync<List<LeaguePlayerHitDto>>("/api/ibba/search?name=noa%20%20cohen"));
        Assert.Equal(("Demo League", "Noa Cohen", "https://demo-league.test/players/77"), (hit.LeagueName, hit.PlayerName, hit.PlayerUrl));

        // Its card: the same preview a pasted link gets.
        var card = await parent.GetAsync<IbbaPreviewDto>($"/api/ibba/preview?playerUrl={Uri.EscapeDataString(hit.PlayerUrl)}");
        Assert.Equal(("Demo Lions", hit.PlayerUrl), (card.Teams.Single().TeamName, card.PlayerUrl));
        Assert.Null(card.AlreadyLinkedTo);

        var created = await parent.PostAsync<CreatePlayerFromIbbaResultDto>("/api/ibba/players", new { ibbaPlayerUrl = card.PlayerUrl });
        var again = await parent.GetAsync<IbbaPreviewDto>($"/api/ibba/preview?playerUrl={Uri.EscapeDataString(hit.PlayerUrl)}");
        Assert.Equal(created.Player.Id, again.AlreadyLinkedTo?.PlayerId);

        Assert.Empty(await parent.GetAsync<List<LeaguePlayerHitDto>>("/api/ibba/search?name=Dana"));
        var tooShort = await parent.Http.GetAsync("/api/ibba/search?name=No");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, tooShort.StatusCode);
    }
}
