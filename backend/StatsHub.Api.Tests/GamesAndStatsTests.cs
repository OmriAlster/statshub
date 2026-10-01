using System.Net;
using System.Net.Http.Json;
using StatsHub.Api.DTOs;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

public class GamesAndStatsTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public GamesAndStatsTests(StatsHubFactory factory) => _factory = factory;

    private async Task<(TestUser parent, PlayerDto player, TeamDto team)> ParentWithPlayerOnTeamAsync()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var team = await parent.CreateTeamWithPlayerAsync(player.Id, "Hawks", jersey: 7);
        return (parent, player, team);
    }

    // ---- Games ----

    [Fact]
    public async Task A_new_game_is_listed_for_the_player_and_defaults_to_home()
    {
        var (parent, player, team) = await ParentWithPlayerOnTeamAsync();
        var game = await parent.CreateGameAsync(team.Id, opponent: "Eagles");

        Assert.Equal("Upcoming", game.Status);
        Assert.Equal(team.Id, game.TeamId);
        Assert.True(game.IsHomeGame);
        var games = await parent.GetAsync<List<GameDto>>($"/api/games/player/{player.Id}");
        Assert.Contains(games, g => g.Id == game.Id && g.OpponentName == "Eagles");
    }

    [Fact]
    public async Task Editing_a_game_updates_home_away_score_and_status()
    {
        var (parent, _, team) = await ParentWithPlayerOnTeamAsync();
        var game = await parent.CreateGameAsync(team.Id);

        var updated = await parent.PutAsync<GameDto>($"/api/games/{game.Id}", new UpdateGameDto { IsHomeGame = false, Status = "Completed", TeamScore = 70, OpponentScore = 64, Location = "Away gym" });

        Assert.False(updated.IsHomeGame);
        Assert.Equal("Completed", updated.Status);
        Assert.Equal(70, updated.TeamScore);
        Assert.Equal(64, updated.OpponentScore);
        Assert.Equal("Away gym", updated.Location);
    }

    [Fact]
    public async Task Deleting_a_game_removes_it_and_its_box_score()
    {
        var (parent, player, team) = await ParentWithPlayerOnTeamAsync();
        var game = await parent.CreateGameAsync(team.Id);
        var stats = await parent.CreateStatsAsync(game.Id, player.Id, ftm: 2, fta: 2);

        Assert.Equal(HttpStatusCode.NoContent, (await parent.Http.DeleteAsync($"/api/games/{game.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.Http.GetAsync($"/api/games/{game.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.Http.GetAsync($"/api/gamestats/{stats.Id}")).StatusCode);
        Assert.DoesNotContain(await parent.GetAsync<List<GameDto>>($"/api/games/player/{player.Id}"), g => g.Id == game.Id);
    }

    // ---- Box scores ----

    [Fact]
    public async Task Points_and_rebounds_are_derived_from_the_box_score()
    {
        var (parent, player, team) = await ParentWithPlayerOnTeamAsync();
        var game = await parent.CreateGameAsync(team.Id);
        var stats = await parent.PostAsync<GameStatsDto>("/api/gamestats", new CreateGameStatsDto
        {
            GameId = game.Id, PlayerId = player.Id,
            FieldGoalsMade = 3, FieldGoalsAttempted = 6,
            ThreePointersMade = 2, ThreePointersAttempted = 4,
            FreeThrowsMade = 1, FreeThrowsAttempted = 2,
            OffensiveRebounds = 2, DefensiveRebounds = 3,
        });

        Assert.Equal(3 * 2 + 2 * 3 + 1, stats.TotalPoints);
        Assert.Equal(5, stats.TotalRebounds);
        Assert.Equal(50, stats.FieldGoalPercentage);
    }

    [Fact]
    public async Task A_player_gets_only_one_box_score_per_game()
    {
        var (parent, player, team) = await ParentWithPlayerOnTeamAsync();
        var game = await parent.CreateGameAsync(team.Id);
        await parent.CreateStatsAsync(game.Id, player.Id, ftm: 2, fta: 2);

        var second = await parent.Http.PostAsJsonAsync("/api/gamestats", new CreateGameStatsDto { GameId = game.Id, PlayerId = player.Id, FreeThrowsMade = 5, FreeThrowsAttempted = 5 });
        Assert.False(second.IsSuccessStatusCode, "a second box score for the same player and game was accepted - it would be double-counted in season stats");

        var season = (await parent.GetAsync<List<PlayerTeamStatsDto>>($"/api/gamestats/player/{player.Id}")).Single();
        Assert.Equal(1, season.GamesPlayed);
    }

    [Fact]
    public async Task A_box_score_cannot_be_attached_to_a_game_that_does_not_exist()
    {
        var (parent, player, _) = await ParentWithPlayerOnTeamAsync();
        var response = await parent.Http.PostAsJsonAsync("/api/gamestats", new CreateGameStatsDto { GameId = Guid.NewGuid(), PlayerId = player.Id });
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task A_box_score_cannot_be_attached_to_another_familys_game()
    {
        var victim = await TestUser.SignInAsync(_factory, "victim");
        var victimPlayer = await victim.CreatePlayerAsync();
        var victimTeam = await victim.CreateTeamWithPlayerAsync(victimPlayer.Id);
        var victimGame = await victim.CreateGameAsync(victimTeam.Id, opponent: "Secret Opponent");

        var (attacker, attackerPlayer, _) = await ParentWithPlayerOnTeamAsync();
        var attach = await attacker.Http.PostAsJsonAsync("/api/gamestats", new CreateGameStatsDto { GameId = victimGame.Id, PlayerId = attackerPlayer.Id });
        Assert.False(attach.IsSuccessStatusCode, "attached a box score to another family's game");

        // And even if one existed, the other family's game must not leak into this player's games.
        var games = await attacker.GetAsync<List<GameDto>>($"/api/games/player/{attackerPlayer.Id}");
        Assert.DoesNotContain(games, g => g.Id == victimGame.Id);
    }

    [Fact]
    public async Task Shots_update_the_box_score_and_removing_one_reverses_it()
    {
        var (parent, player, team) = await ParentWithPlayerOnTeamAsync();
        var game = await parent.CreateGameAsync(team.Id);
        var stats = await parent.CreateStatsAsync(game.Id, player.Id);

        var two = await parent.PostAsync<ShotDto>("/api/shots", new CreateShotDto { GameStatsId = stats.Id, Quarter = 1, X = 0.5, Y = 0.3, Made = true, Value = 2 });
        await parent.PostAsync<ShotDto>("/api/shots", new CreateShotDto { GameStatsId = stats.Id, Quarter = 2, X = 0.1, Y = 0.8, Made = true, Value = 3 });
        await parent.PostAsync<ShotDto>("/api/shots", new CreateShotDto { GameStatsId = stats.Id, Quarter = 2, X = 0.2, Y = 0.8, Made = false, Value = 3 });

        var afterShots = await parent.GetAsync<GameStatsDto>($"/api/gamestats/{stats.Id}");
        Assert.Equal((1, 1), (afterShots.FieldGoalsMade, afterShots.FieldGoalsAttempted));
        Assert.Equal((1, 2), (afterShots.ThreePointersMade, afterShots.ThreePointersAttempted));
        Assert.Equal(5, afterShots.TotalPoints);

        Assert.Equal(HttpStatusCode.NoContent, (await parent.Http.DeleteAsync($"/api/shots/{two.Id}")).StatusCode);
        var afterDelete = await parent.GetAsync<GameStatsDto>($"/api/gamestats/{stats.Id}");
        Assert.Equal((0, 0), (afterDelete.FieldGoalsMade, afterDelete.FieldGoalsAttempted));
        Assert.Equal(3, afterDelete.TotalPoints);
        Assert.Equal(2, (await parent.GetAsync<List<ShotDto>>($"/api/shots/gamestats/{stats.Id}")).Count);
    }

    [Theory]
    [InlineData(4, 1)]  // there's no 4-point shot
    [InlineData(2, 9)]  // there's no 9th quarter
    public async Task Invalid_shots_are_rejected(int value, int quarter)
    {
        var (parent, player, team) = await ParentWithPlayerOnTeamAsync();
        var game = await parent.CreateGameAsync(team.Id);
        var stats = await parent.CreateStatsAsync(game.Id, player.Id);

        var response = await parent.Http.PostAsJsonAsync("/api/shots", new CreateShotDto { GameStatsId = stats.Id, Quarter = quarter, X = 0.5, Y = 0.5, Made = true, Value = value });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Season stats ----

    [Fact]
    public async Task Season_averages_cover_every_game_with_a_box_score()
    {
        var (parent, player, team) = await ParentWithPlayerOnTeamAsync();
        var g1 = await parent.CreateGameAsync(team.Id, date: DateTime.UtcNow.AddDays(-7));
        var g2 = await parent.CreateGameAsync(team.Id, date: DateTime.UtcNow.AddDays(-3));
        await parent.CreateStatsAsync(g1.Id, player.Id, ftm: 10, fta: 10, reb: 4, ast: 2);
        await parent.CreateStatsAsync(g2.Id, player.Id, ftm: 0, fta: 0, reb: 6, ast: 4);

        var season = (await parent.GetAsync<List<PlayerTeamStatsDto>>($"/api/gamestats/player/{player.Id}")).Single();
        Assert.Equal(2, season.GamesPlayed);
        Assert.Equal(10, season.TotalPoints);
        Assert.Equal(5, season.PointsPerGame);
        Assert.Equal(5, season.ReboundsPerGame);
        Assert.Equal(3, season.AssistsPerGame);
        Assert.Equal(7, season.JerseyNumber);
    }

    [Fact]
    public async Task Friendly_games_never_count_toward_season_stats_or_shot_charts()
    {
        var (parent, player, team) = await ParentWithPlayerOnTeamAsync();
        var league = await parent.CreateGameAsync(team.Id, "League");
        var friendly = await parent.CreateGameAsync(team.Id, "Friendly");
        await parent.CreateStatsAsync(league.Id, player.Id, ftm: 4, fta: 4);
        var friendlyStats = await parent.CreateStatsAsync(friendly.Id, player.Id, ftm: 20, fta: 20);
        await parent.PostAsync<ShotDto>("/api/shots", new CreateShotDto { GameStatsId = friendlyStats.Id, Quarter = 1, X = 0.5, Y = 0.5, Made = true, Value = 2 });

        var season = (await parent.GetAsync<List<PlayerTeamStatsDto>>($"/api/gamestats/player/{player.Id}")).Single();
        Assert.Equal(1, season.GamesPlayed);
        Assert.Equal(4, season.TotalPoints);
        Assert.Empty(await parent.GetAsync<List<ShotDto>>($"/api/shots/player/{player.Id}/team/{team.Id}"));

        // The friendly's own box score is still there.
        var friendlyGame = await parent.GetAsync<GameDto>($"/api/games/{friendly.Id}");
        Assert.Equal(22, friendlyGame.PlayerStats.Single().TotalPoints);
    }

    [Fact]
    public async Task Season_game_count_includes_each_teams_games_once()
    {
        var (parent, player, team) = await ParentWithPlayerOnTeamAsync();
        await parent.CreateGameAsync(team.Id);
        await parent.CreateGameAsync(team.Id);
        var seasons = await parent.GetAsync<List<SeasonDto>>("/api/seasons");
        Assert.Equal(2, seasons.Single().TotalGames);
    }
}
