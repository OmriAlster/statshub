using System.Net;
using System.Net.Http.Json;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

// A player signed in as themself sees their IBBA link, but connecting,
// syncing and disconnecting are their parents'.
public class IbbaLinkAccessTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public IbbaLinkAccessTests(StatsHubFactory factory) => _factory = factory;

    [Fact]
    public async Task The_player_sees_the_link_but_cannot_change_it()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var self = await TestUser.SignInAsync(_factory, "kid");
        await _factory.WithDbAsync(async db =>
        {
            db.Players.Single(p => p.Id == player.Id).LinkedUserId = self.User.Id;
            db.PlayerIbbaLinks.Add(new PlayerIbbaLink { PlayerId = player.Id, IbbaPlayerUrl = "https://ibasketball.co.il/player/206ad122e135e4e5-66890422/" });
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.OK, (await self.Http.GetAsync($"/api/players/{player.Id}/ibba")).StatusCode);

        var link = await self.Http.PostAsJsonAsync($"/api/players/{player.Id}/ibba/link", new LinkIbbaPlayerDto { IbbaPlayerUrl = "https://ibasketball.co.il/player/2069e3b4f2f47c8f-1234/" });
        var sync = await self.Http.PostAsync($"/api/players/{player.Id}/ibba/sync", null);
        var unlink = await self.Http.DeleteAsync($"/api/players/{player.Id}/ibba/link");
        Assert.False(link.IsSuccessStatusCode);
        Assert.False(sync.IsSuccessStatusCode);
        Assert.False(unlink.IsSuccessStatusCode);

        // Still linked, to the same player page.
        var status = await parent.GetAsync<IbbaLinkStatusDto>($"/api/players/{player.Id}/ibba");
        Assert.Equal("https://ibasketball.co.il/player/206ad122e135e4e5-66890422/", status.IbbaPlayerUrl);
    }
}
