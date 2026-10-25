using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using StatsHub.Api.DTOs;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

// Screens watching a player hear about a change to their game the moment it's
// saved - from any write - and only screens allowed to see that player.
public class LiveHubTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public LiveHubTests(StatsHubFactory factory) => _factory = factory;

    private async Task<(HubConnection Connection, List<Guid> Changed)> ConnectAsync(TestUser? user)
    {
        var token = user?.Http.DefaultRequestHeaders.Authorization?.Parameter;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/live"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (token != null) options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        var changed = new List<Guid>();
        connection.On<Guid>("GameChanged", id => { lock (changed) changed.Add(id); });
        await connection.StartAsync();
        return (connection, changed);
    }

    private static async Task<bool> HeardAsync(List<Guid> changed, Guid gameId)
    {
        for (var i = 0; i < 50; i++)
        {
            lock (changed) if (changed.Contains(gameId)) return true;
            await Task.Delay(100);
        }
        return false;
    }

    [Fact]
    public async Task A_parent_watching_a_player_hears_each_change_to_their_game()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var team = await parent.CreateTeamWithPlayerAsync(player.Id);
        var game = await parent.CreateGameAsync(team.Id);

        var (watcher, changed) = await ConnectAsync(parent);
        await watcher.InvokeAsync("WatchPlayer", player.Id);

        var stats = await parent.CreateStatsAsync(game.Id, player.Id, ftm: 2, fta: 3);  // a box score
        Assert.True(await HeardAsync(changed, game.Id));

        lock (changed) changed.Clear();
        await parent.PostAsync<ShotDto>("/api/shots", new CreateShotDto { GameStatsId = stats.Id, Quarter = 1, X = 0.5, Y = 0.3, Made = true, Value = 2 });  // a shot
        Assert.True(await HeardAsync(changed, game.Id));

        await watcher.DisposeAsync();
    }

    [Fact]
    public async Task Someone_else_cannot_watch_a_player()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var stranger = await TestUser.SignInAsync(_factory, "stranger");

        var (strangerConnection, _) = await ConnectAsync(stranger);
        await Assert.ThrowsAsync<HubException>(() => strangerConnection.InvokeAsync("WatchPlayer", player.Id));

        var (anonymous, _) = await ConnectAsync(null);
        await Assert.ThrowsAsync<HubException>(() => anonymous.InvokeAsync("WatchPlayer", player.Id));

        await strangerConnection.DisposeAsync();
        await anonymous.DisposeAsync();
    }

    [Fact]
    public async Task A_share_link_viewer_hears_changes_without_signing_in()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var team = await parent.CreateTeamWithPlayerAsync(player.Id);
        var game = await parent.CreateGameAsync(team.Id);
        var share = await parent.PostAsync<ShareLinkDto>("/api/share", new { playerId = player.Id });

        var (viewer, changed) = await ConnectAsync(null);
        await viewer.InvokeAsync("WatchShare", share.Token);
        await Assert.ThrowsAsync<HubException>(() => viewer.InvokeAsync("WatchShare", "not-a-real-token"));

        await parent.PutAsync<GameDto>($"/api/games/{game.Id}", new { status = "In Progress" });
        Assert.True(await HeardAsync(changed, game.Id));

        await viewer.DisposeAsync();
    }
}
