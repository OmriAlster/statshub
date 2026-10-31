using System.Net;
using System.Net.Http.Json;
using StatsHub.Api.DTOs;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

// A player signed in as themself sees everything their parents' pages show
// about them - their games (with their own team's name), each game's page,
// their season - but changes nothing.
public class PlayerSignInTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public PlayerSignInTests(StatsHubFactory factory) => _factory = factory;

    private async Task<(TestUser Parent, TestUser Self, PlayerDto Player, TeamDto Team)> FamilyAsync()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var team = await parent.CreateTeamWithPlayerAsync(player.Id, "Hawks");
        var self = await TestUser.SignInAsync(_factory, "kid");
        await _factory.WithDbAsync(async db =>
        {
            db.Players.Single(p => p.Id == player.Id).LinkedUserId = self.User.Id;
            await db.SaveChangesAsync();
        });
        return (parent, self, player, team);
    }

    [Fact]
    public async Task Every_game_of_the_team_opens_with_the_teams_name()
    {
        var (parent, self, player, team) = await FamilyAsync();
        var noStats = await parent.CreateGameAsync(team.Id, opponent: "Eagles");
        var withStats = await parent.CreateGameAsync(team.Id, opponent: "Lions");
        await parent.CreateStatsAsync(withStats.Id, player.Id, ftm: 2, fta: 2);

        var list = await self.GetAsync<List<GameDto>>($"/api/games/player/{player.Id}");
        Assert.Equal(2, list.Count);
        Assert.All(list, g => Assert.Equal("Hawks", g.TeamName));

        foreach (var id in new[] { noStats.Id, withStats.Id })
        {
            var game = await self.GetAsync<GameDto>($"/api/games/{id}?playerId={player.Id}");
            Assert.Equal("Hawks", game.TeamName);
        }
        Assert.Equal(HttpStatusCode.OK, (await self.Http.GetAsync($"/api/games/{noStats.Id}/matchup?playerId={player.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await self.Http.GetAsync($"/api/gamestats/player/{player.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_player_cannot_change_or_delete_a_game()
    {
        var (parent, self, player, team) = await FamilyAsync();
        var game = await parent.CreateGameAsync(team.Id);

        Assert.False((await self.Http.DeleteAsync($"/api/games/{game.Id}")).IsSuccessStatusCode);
        Assert.False((await self.Http.PutAsJsonAsync($"/api/players/{player.Id}/goals", new[] { new { stat = "ppg", target = 10 } })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.Http.GetAsync($"/api/games/{game.Id}")).StatusCode); // still there
    }
}
