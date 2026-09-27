using System.Net;
using System.Net.Http.Json;
using StatsHub.Api.DTOs;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

// Families must never see or change each other's data.
public class AccessControlTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public AccessControlTests(StatsHubFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/players")]
    [InlineData("/api/teams")]
    [InlineData("/api/seasons")]
    [InlineData("/api/games/player/1")]
    [InlineData("/api/games/1")]
    [InlineData("/api/gamestats/player/1")]
    [InlineData("/api/auth/me")]
    public async Task Protected_endpoints_require_sign_in(string url)
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dev_login_returns_a_working_token()
    {
        var parent = await TestUser.SignInAsync(_factory, "alice");
        var me = await parent.GetAsync<UserDto>("/api/auth/me");
        Assert.Equal(parent.User.Id, me.Id);
    }

    [Fact]
    public async Task Another_family_cannot_see_or_change_a_player()
    {
        var owner = await TestUser.SignInAsync(_factory, "owner");
        var stranger = await TestUser.SignInAsync(_factory, "stranger");
        var player = await owner.CreatePlayerAsync();

        var players = await stranger.GetAsync<List<PlayerDto>>("/api/players");
        Assert.DoesNotContain(players, p => p.Id == player.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/players/{player.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.DeleteAsync($"/api/players/{player.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.PostAsJsonAsync($"/api/players/{player.Id}/parent-invite", new CreateParentInviteDto())).StatusCode);

        // Still there for the owner.
        Assert.Equal(HttpStatusCode.OK, (await owner.Http.GetAsync($"/api/players/{player.Id}")).StatusCode);
    }

    [Fact]
    public async Task Another_family_cannot_see_or_change_games_or_stats()
    {
        var owner = await TestUser.SignInAsync(_factory, "owner");
        var stranger = await TestUser.SignInAsync(_factory, "stranger");
        var player = await owner.CreatePlayerAsync();
        var team = await owner.CreateTeamWithPlayerAsync(player.Id);
        var game = await owner.CreateGameAsync(team.Id);
        var stats = await owner.CreateStatsAsync(game.Id, player.Id, ftm: 3, fta: 4);

        Assert.Empty(await stranger.GetAsync<List<GameDto>>($"/api/games/player/{player.Id}"));
        Assert.Empty(await stranger.GetAsync<List<GameDto>>($"/api/games/team/{team.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/games/{game.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.PutAsJsonAsync($"/api/games/{game.Id}", new UpdateGameDto { Status = "Completed", TeamScore = 1, OpponentScore = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.DeleteAsync($"/api/games/{game.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/gamestats/{stats.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.PutAsJsonAsync($"/api/gamestats/{stats.Id}", new UpdateGameStatsDto { FreeThrowsMade = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.DeleteAsync($"/api/gamestats/{stats.Id}")).StatusCode);

        // Nothing actually changed.
        var after = await owner.GetAsync<GameStatsDto>($"/api/gamestats/{stats.Id}");
        Assert.Equal(3, after.FreeThrowsMade);
    }

    [Fact]
    public async Task Creating_a_game_on_someone_elses_team_is_refused_with_403()
    {
        var owner = await TestUser.SignInAsync(_factory, "owner");
        var stranger = await TestUser.SignInAsync(_factory, "stranger");
        var player = await owner.CreatePlayerAsync();
        var team = await owner.CreateTeamWithPlayerAsync(player.Id);

        var response = await stranger.Http.PostAsJsonAsync("/api/games", new CreateGameDto { TeamId = team.Id, OpponentName = "X", GameDate = DateTime.UtcNow });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Recording_stats_for_someone_elses_player_is_refused_with_403()
    {
        var owner = await TestUser.SignInAsync(_factory, "owner");
        var stranger = await TestUser.SignInAsync(_factory, "stranger");
        var player = await owner.CreatePlayerAsync();
        var team = await owner.CreateTeamWithPlayerAsync(player.Id);
        var game = await owner.CreateGameAsync(team.Id);

        var response = await stranger.Http.PostAsJsonAsync("/api/gamestats", new CreateGameStatsDto { GameId = game.Id, PlayerId = player.Id });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Another_family_cannot_rename_or_edit_a_team_roster()
    {
        var owner = await TestUser.SignInAsync(_factory, "owner");
        var stranger = await TestUser.SignInAsync(_factory, "stranger");
        var player = await owner.CreatePlayerAsync();
        var team = await owner.CreateTeamWithPlayerAsync(player.Id);
        var strangerPlayer = await stranger.CreatePlayerAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.PutAsJsonAsync($"/api/teams/{team.Id}", new CreateTeamDto { Name = "Hijacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.DeleteAsync($"/api/teams/{team.Id}/players/{player.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.PostAsJsonAsync($"/api/teams/{team.Id}/players/{strangerPlayer.Id}", new AddPlayerToTeamDto())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.DeleteAsync($"/api/teams/{team.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_share_link_is_public_but_only_for_what_was_shared()
    {
        var owner = await TestUser.SignInAsync(_factory, "owner");
        var player = await owner.CreatePlayerAsync("Shared");
        var link = await owner.PostAsync<ShareLinkDto>("/api/share", new CreateShareLinkDto { PlayerId = player.Id });

        var anonymous = _factory.CreateClient();
        var shared = await anonymous.GetFromJsonAsync<SharedPlayerDto>($"/api/share/{link.Token}", TestUser.Json);
        Assert.Equal("Shared Test", shared!.PlayerName);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/share/not-a-real-token")).StatusCode);

        // A stranger can't mint a share link for someone else's player.
        var stranger = await TestUser.SignInAsync(_factory, "stranger");
        var response = await stranger.Http.PostAsJsonAsync("/api/share", new CreateShareLinkDto { PlayerId = player.Id });
        Assert.False(response.IsSuccessStatusCode, $"stranger got {(int)response.StatusCode}");
    }
}
