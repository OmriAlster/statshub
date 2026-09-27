using System.Net;
using System.Net.Http.Json;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

public class FamiliesAndLiveGamesTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public FamiliesAndLiveGamesTests(StatsHubFactory factory) => _factory = factory;

    // ---- Co-parents ----

    [Fact]
    public async Task An_invited_co_parent_sees_and_edits_the_same_player()
    {
        var parent = await TestUser.SignInAsync(_factory, "mom");
        var player = await parent.CreatePlayerAsync();
        var team = await parent.CreateTeamWithPlayerAsync(player.Id);
        var game = await parent.CreateGameAsync(team.Id);

        var coParent = await parent.InviteCoParentAsync(_factory, player.Id);

        Assert.Contains(await coParent.GetAsync<List<PlayerDto>>("/api/players"), p => p.Id == player.Id);
        Assert.Contains(await coParent.GetAsync<List<GameDto>>($"/api/games/player/{player.Id}"), g => g.Id == game.Id);
        var stats = await coParent.CreateStatsAsync(game.Id, player.Id, ftm: 1, fta: 1);
        Assert.Equal(1, stats.TotalPoints);
    }

    [Fact]
    public async Task A_parent_invite_code_works_once()
    {
        var parent = await TestUser.SignInAsync(_factory, "mom");
        var player = await parent.CreatePlayerAsync();
        var invite = await parent.PostAsync<ParentInviteDto>($"/api/players/{player.Id}/parent-invite", new CreateParentInviteDto());

        var first = await TestUser.SignInAsync(_factory, "dad");
        Assert.True((await first.Http.PostAsJsonAsync("/api/players/claim-parent-invite", new ClaimParentInviteDto { InviteCode = invite.InviteCode })).IsSuccessStatusCode);

        var second = await TestUser.SignInAsync(_factory, "stranger");
        var reuse = await second.Http.PostAsJsonAsync("/api/players/claim-parent-invite", new ClaimParentInviteDto { InviteCode = invite.InviteCode });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.DoesNotContain(await second.GetAsync<List<PlayerDto>>("/api/players"), p => p.Id == player.Id);
    }

    [Fact]
    public async Task A_team_can_be_renamed_by_its_parents()
    {
        var parent = await TestUser.SignInAsync(_factory, "mom");
        var player = await parent.CreatePlayerAsync();
        var team = await parent.CreateTeamWithPlayerAsync(player.Id, "Old Name");
        var coParent = await parent.InviteCoParentAsync(_factory, player.Id);

        var renamed = await coParent.PutAsync<TeamDto>($"/api/teams/{team.Id}", new CreateTeamDto { Name = "  New Name  " });
        Assert.Equal("New Name", renamed.Name);
        Assert.Equal("New Name", (await parent.GetAsync<PlayerDto>($"/api/players/{player.Id}")).Teams.Single().Name);

        Assert.Equal(HttpStatusCode.BadRequest, (await parent.Http.PutAsJsonAsync($"/api/teams/{team.Id}", new CreateTeamDto { Name = " " })).StatusCode);
    }

    // ---- Live games: whoever starts it records, everyone else watches ----

    private async Task<(TestUser tracker, TestUser watcher, PlayerDto player, GameDto game, GameStatsDto stats)> LiveGameAsync()
    {
        var tracker = await TestUser.SignInAsync(_factory, "tracker");
        var player = await tracker.CreatePlayerAsync();
        var team = await tracker.CreateTeamWithPlayerAsync(player.Id);
        var game = await tracker.CreateGameAsync(team.Id, date: DateTime.UtcNow);
        var stats = await tracker.CreateStatsAsync(game.Id, player.Id);
        await tracker.PutAsync<GameDto>($"/api/games/{game.Id}", new UpdateGameDto { Status = "In Progress" });
        var watcher = await tracker.InviteCoParentAsync(_factory, player.Id);
        return (tracker, watcher, player, game, stats);
    }

    [Fact]
    public async Task Only_the_parent_who_started_a_live_game_can_record_it()
    {
        var (tracker, watcher, _, game, stats) = await LiveGameAsync();

        Assert.True((await tracker.GetAsync<GameDto>($"/api/games/{game.Id}")).CanRecordLive);
        Assert.False((await watcher.GetAsync<GameDto>($"/api/games/{game.Id}")).CanRecordLive);

        Assert.False((await watcher.Http.PutAsJsonAsync($"/api/gamestats/{stats.Id}", new UpdateGameStatsDto { FreeThrowsMade = 9 })).IsSuccessStatusCode);
        Assert.False((await watcher.Http.PutAsJsonAsync($"/api/games/{game.Id}", new UpdateGameDto { Status = "Completed", TeamScore = 1, OpponentScore = 2 })).IsSuccessStatusCode);

        // The tracker still can.
        var updated = await tracker.PutAsync<GameStatsDto>($"/api/gamestats/{stats.Id}", new UpdateGameStatsDto { FreeThrowsMade = 2, FreeThrowsAttempted = 2 });
        Assert.Equal(2, updated.TotalPoints);
    }

    [Fact]
    public async Task A_watcher_cannot_add_or_remove_shots_in_a_live_game_either()
    {
        var (tracker, watcher, _, _, stats) = await LiveGameAsync();
        var trackerShot = await tracker.PostAsync<ShotDto>("/api/shots", new CreateShotDto { GameStatsId = stats.Id, Quarter = 1, X = 0.5, Y = 0.5, Made = true, Value = 2 });

        var add = await watcher.Http.PostAsJsonAsync("/api/shots", new CreateShotDto { GameStatsId = stats.Id, Quarter = 1, X = 0.4, Y = 0.4, Made = true, Value = 3 });
        Assert.False(add.IsSuccessStatusCode, "a watcher added a shot to someone else's live game");
        var remove = await watcher.Http.DeleteAsync($"/api/shots/{trackerShot.Id}");
        Assert.False(remove.IsSuccessStatusCode, "a watcher removed a shot from someone else's live game");

        var box = await tracker.GetAsync<GameStatsDto>($"/api/gamestats/{stats.Id}");
        Assert.Equal(2, box.TotalPoints);
    }

    [Fact]
    public async Task Once_the_game_ends_every_parent_can_edit_again()
    {
        var (tracker, watcher, _, game, stats) = await LiveGameAsync();
        await tracker.FinishGameAsync(game.Id, 50, 40);

        Assert.True((await watcher.GetAsync<GameDto>($"/api/games/{game.Id}")).CanRecordLive);
        var edited = await watcher.PutAsync<GameStatsDto>($"/api/gamestats/{stats.Id}", new UpdateGameStatsDto { FreeThrowsMade = 3, FreeThrowsAttempted = 3 });
        Assert.Equal(3, edited.TotalPoints);
    }

    [Fact]
    public async Task A_player_can_only_be_in_one_live_game_at_a_time()
    {
        var (tracker, _, player, game, _) = await LiveGameAsync();
        var team = (await tracker.GetAsync<PlayerDto>($"/api/players/{player.Id}")).Teams.Single();
        var other = await tracker.CreateGameAsync(team.Id, date: DateTime.UtcNow);

        var response = await tracker.Http.PostAsJsonAsync("/api/gamestats", new CreateGameStatsDto { GameId = other.Id, PlayerId = player.Id });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- Shared IBBA games: two of your teams linked to the same real team ----

    [Fact]
    public async Task A_shared_ibba_game_shows_as_each_players_own_team()
    {
        var parent = await TestUser.SignInAsync(_factory, "mom");
        var older = await parent.CreatePlayerAsync("Older");
        var younger = await parent.CreatePlayerAsync("Younger");
        var olderTeam = await parent.CreateTeamWithPlayerAsync(older.Id, "Older's team");
        var youngerTeam = await parent.CreateTeamWithPlayerAsync(younger.Id, "Younger's team");

        int gameId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var ours = new IbbaTeam { IbbaTeamId = $"t{Guid.NewGuid():N}", TeamUrl = $"https://example.test/{Guid.NewGuid():N}", Name = "Maccabi", LeagueUrl = "", LeagueName = "" };
            var theirs = new IbbaTeam { IbbaTeamId = $"t{Guid.NewGuid():N}", TeamUrl = $"https://example.test/{Guid.NewGuid():N}", Name = "Hapoel", LeagueUrl = "", LeagueName = "" };
            db.IbbaTeams.AddRange(ours, theirs);
            await db.SaveChangesAsync();
            // Both siblings' app teams link to the same real team - the
            // younger one's has the HIGHER id, so "first found" would be wrong.
            (await db.Teams.FindAsync(olderTeam.Id))!.IbbaTeamId = ours.Id;
            (await db.Teams.FindAsync(youngerTeam.Id))!.IbbaTeamId = ours.Id;
            var game = new Game { IbbaGameCode = $"c{Guid.NewGuid():N}", HomeTeamId = theirs.Id, AwayTeamId = ours.Id, GameDate = DateTime.UtcNow.AddDays(3), Status = "Upcoming", GameType = "League", Location = "Gym" };
            db.Games.Add(game);
            await db.SaveChangesAsync();
            gameId = game.Id;
        });

        var asYounger = await parent.GetAsync<GameDto>($"/api/games/{gameId}?playerId={younger.Id}");
        Assert.Equal(youngerTeam.Id, asYounger.TeamId);
        Assert.False(asYounger.IsHomeGame);
        Assert.Equal("Hapoel", asYounger.OpponentName);

        var asOlder = await parent.GetAsync<GameDto>($"/api/games/{gameId}?playerId={older.Id}");
        Assert.Equal(olderTeam.Id, asOlder.TeamId);

        Assert.Contains(await parent.GetAsync<List<GameDto>>($"/api/games/player/{younger.Id}"), g => g.Id == gameId && g.TeamId == youngerTeam.Id);

        // Saving the final score as the younger player's team lands on the
        // right side of the fixture (away), not the opponent's.
        var finished = await parent.PutAsync<GameDto>($"/api/games/{gameId}?playerId={younger.Id}", new UpdateGameDto { Status = "Completed", TeamScore = 80, OpponentScore = 60 });
        Assert.Equal((80, 60), (finished.TeamScore, finished.OpponentScore));
        await _factory.WithDbAsync(async db =>
        {
            var stored = (await db.Games.FindAsync(gameId))!;
            Assert.Equal((60, 80), (stored.HomeScore, stored.AwayScore));
        });
    }

    [Fact]
    public async Task Deleting_a_team_keeps_shared_ibba_games_still_needed_by_another_team()
    {
        var parent = await TestUser.SignInAsync(_factory, "mom");
        var a = await parent.CreatePlayerAsync("A");
        var b = await parent.CreatePlayerAsync("B");
        var teamA = await parent.CreateTeamWithPlayerAsync(a.Id, "A team");
        var teamB = await parent.CreateTeamWithPlayerAsync(b.Id, "B team");
        var manual = await parent.CreateGameAsync(teamA.Id);

        int sharedGameId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var ours = new IbbaTeam { IbbaTeamId = $"t{Guid.NewGuid():N}", TeamUrl = $"https://example.test/{Guid.NewGuid():N}", Name = "Shared", LeagueUrl = "", LeagueName = "" };
            db.IbbaTeams.Add(ours);
            await db.SaveChangesAsync();
            (await db.Teams.FindAsync(teamA.Id))!.IbbaTeamId = ours.Id;
            (await db.Teams.FindAsync(teamB.Id))!.IbbaTeamId = ours.Id;
            var game = new Game { IbbaGameCode = $"c{Guid.NewGuid():N}", HomeTeamId = ours.Id, GameDate = DateTime.UtcNow, Status = "Upcoming", GameType = "League", Location = "" };
            db.Games.Add(game);
            await db.SaveChangesAsync();
            sharedGameId = game.Id;
        });

        Assert.Equal(HttpStatusCode.NoContent, (await parent.Http.DeleteAsync($"/api/teams/{teamA.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await parent.Http.GetAsync($"/api/games/{manual.Id}")).StatusCode);
        Assert.Contains(await parent.GetAsync<List<GameDto>>($"/api/games/player/{b.Id}"), g => g.Id == sharedGameId);
    }
}
