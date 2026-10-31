using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StatsHub.Api.DTOs;
using StatsHub.Api.Services;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

// What a parent sees around a game: the opponent and their earlier meetings,
// the games in the phone's calendar, and a season high told by notification.
public class ParentFeaturesTests : IClassFixture<ParentFeaturesTests.Factory>
{
    public sealed class Factory : StatsHubFactory
    {
        public RecordingPush Push { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton<IPushNotificationService>(Push));
        }
    }

    public sealed class RecordingPush : IPushNotificationService
    {
        public List<(List<Guid> Users, string Title, string Body)> ToUsers { get; } = [];
        public Task NotifyTeamAsync(Guid teamId, string title, string body, string? url = null, Guid? excludeUserId = null, DateTime? gameDate = null) => Task.CompletedTask;
        public Task NotifyIbbaTeamAsync(Guid ibbaTeamId, string title, string body, string? url = null, Guid? excludeUserId = null, DateTime? gameDate = null) => Task.CompletedTask;
        public Task NotifyUsersAsync(IEnumerable<Guid> userIds, string title, string body, string? url = null, DateTime? gameDate = null)
        {
            lock (ToUsers) ToUsers.Add((userIds.ToList(), title, body));
            return Task.CompletedTask;
        }
    }

    private readonly Factory _factory;
    public ParentFeaturesTests(Factory factory) => _factory = factory;

    private async Task<(TestUser Parent, PlayerDto Player, TeamDto Team)> ParentWithPlayerAsync(string firstName = "Noa")
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync(firstName);
        var team = await parent.CreateTeamWithPlayerAsync(player.Id, "Hawks", jersey: 7);
        return (parent, player, team);
    }

    private static async Task<GameDto> PlayedAsync(TestUser parent, Guid teamId, Guid playerId, string opponent, int daysAgo, int ftm, int us, int them, int reb = 0)
    {
        var game = await parent.CreateGameAsync(teamId, opponent: opponent, date: DateTime.UtcNow.AddDays(-daysAgo));
        await parent.CreateStatsAsync(game.Id, playerId, ftm: ftm, fta: ftm, reb: reb);
        return await parent.FinishGameAsync(game.Id, us, them);
    }

    [Fact]
    public async Task Before_a_game_the_earlier_meetings_with_that_opponent_show_with_the_players_line()
    {
        var (parent, player, team) = await ParentWithPlayerAsync();
        var first = await PlayedAsync(parent, team.Id, player.Id, "Eagles", 20, ftm: 9, us: 70, them: 55);
        var second = await PlayedAsync(parent, team.Id, player.Id, "eagles ", 10, ftm: 4, us: 58, them: 61);
        await PlayedAsync(parent, team.Id, player.Id, "Lions", 5, ftm: 2, us: 60, them: 50);
        var next = await parent.CreateGameAsync(team.Id, opponent: "Eagles", date: DateTime.UtcNow.AddDays(2));

        var matchup = await parent.GetAsync<MatchupDto>($"/api/games/{next.Id}/matchup?playerId={player.Id}");

        Assert.Equal([(second.Id, 58, 61, 4), (first.Id, 70, 55, 9)],
            matchup.Previous.Select(g => (g.GameId, g.TeamScore ?? 0, g.OpponentScore ?? 0, g.Points ?? -1)).ToArray());
        Assert.Null(matchup.Opponent); // a game made in the app has no league table
    }

    [Fact]
    public async Task Someone_else_cant_see_a_games_matchup()
    {
        var (parent, player, team) = await ParentWithPlayerAsync();
        var game = await parent.CreateGameAsync(team.Id, opponent: "Eagles", date: DateTime.UtcNow.AddDays(2));
        var stranger = await TestUser.SignInAsync(_factory);

        var response = await stranger.Http.GetAsync($"/api/games/{game.Id}/matchup?playerId={player.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_games_are_a_calendar_a_phone_subscribes_to_without_signing_in()
    {
        var (parent, _, team) = await ParentWithPlayerAsync("Maya");
        await parent.PostAsync<GameDto>("/api/games", new CreateGameDto { TeamId = team.Id, OpponentName = "Eagles, North", GameDate = DateTime.UtcNow.AddDays(3), Location = "Gym 4, Haifa", GameType = "Cup", IsHomeGame = false });

        var link = await parent.GetAsync<CalendarLink>("/api/calendar/link");
        Assert.StartsWith("webcal://", link.WebcalUrl);

        using var anonymous = _factory.CreateClient();
        var ics = await anonymous.GetStringAsync(new Uri(link.Url).AbsolutePath);
        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains("SUMMARY:🏀 Maya vs Eagles\\, North (Cup)", ics);
        Assert.Contains("LOCATION:Gym 4\\, Haifa", ics);
        Assert.Contains("Away", ics);

        var path = new Uri(link.Url).AbsolutePath;
        var lastChar = path[^5];
        var forged = await anonymous.GetAsync(path[..^5] + (lastChar == '0' ? '1' : '0') + ".ics"); // one character of the key changed
        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
    }

    private record CalendarLink(string Url, string WebcalUrl);

    [Fact]
    public async Task A_season_high_is_told_to_the_family_when_the_game_is_final()
    {
        var (parent, player, team) = await ParentWithPlayerAsync("Liam");
        await PlayedAsync(parent, team.Id, player.Id, "Eagles", 9, ftm: 8, us: 60, them: 50, reb: 3);
        await PlayedAsync(parent, team.Id, player.Id, "Lions", 6, ftm: 10, us: 60, them: 50, reb: 4);

        var game = await parent.CreateGameAsync(team.Id, opponent: "Bears", date: DateTime.UtcNow.AddDays(-1));
        await parent.CreateStatsAsync(game.Id, player.Id, ftm: 15, fta: 15, reb: 2);
        await parent.FinishGameAsync(game.Id, 70, 60);

        var told = Assert.Single(_factory.Push.ToUsers, n => n.Title == "🔥 Season high for Liam!");
        Assert.Equal("15 points (was 10) vs Bears", told.Body);
        Assert.Contains(parent.User.Id, told.Users);
    }

    [Fact]
    public async Task No_season_high_before_a_few_games_have_been_played()
    {
        var (parent, player, team) = await ParentWithPlayerAsync("Ella");
        await PlayedAsync(parent, team.Id, player.Id, "Eagles", 5, ftm: 4, us: 60, them: 50);
        await PlayedAsync(parent, team.Id, player.Id, "Lions", 3, ftm: 20, us: 60, them: 50); // only one game before it

        Assert.DoesNotContain(_factory.Push.ToUsers, n => n.Title.Contains("Ella"));
    }
}
