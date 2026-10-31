using System.Net;
using System.Net.Http.Json;
using StatsHub.Api.Controllers;
using StatsHub.Api.Tests.Infrastructure;
using Goal = StatsHub.Api.Controllers.PlayerGoalsController.GoalDto;

namespace StatsHub.Api.Tests;

public class PlayerGoalsTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public PlayerGoalsTests(StatsHubFactory factory) => _factory = factory;

    [Fact]
    public async Task Goals_are_set_as_a_whole_list_and_read_back()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();

        await parent.PutAsync<List<Goal>>($"/api/players/{player.Id}/goals", new[] { new Goal { Stat = "ppg", Target = 10 }, new Goal { Stat = "ftPct", Target = 70 } });
        var after = await parent.PutAsync<List<Goal>>($"/api/players/{player.Id}/goals", new[] { new Goal { Stat = "ftPct", Target = 75 } });

        var goal = Assert.Single(after); // the points goal was left out - removed
        Assert.Equal(("ftPct", 75.0), (goal.Stat, goal.Target));
        Assert.Equivalent(after, await parent.GetAsync<List<Goal>>($"/api/players/{player.Id}/goals"));
    }

    [Fact]
    public async Task Unknown_stats_and_impossible_targets_are_refused()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();

        var unknown = await parent.Http.PutAsJsonAsync($"/api/players/{player.Id}/goals", new[] { new Goal { Stat = "dunks", Target = 3 } });
        var tooHigh = await parent.Http.PutAsJsonAsync($"/api/players/{player.Id}/goals", new[] { new Goal { Stat = "ftPct", Target = 120 } });

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);
    }

    [Fact]
    public async Task Someone_elses_player_has_no_goals_to_see_or_set()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var stranger = await TestUser.SignInAsync(_factory, "stranger");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/players/{player.Id}/goals")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.PutAsJsonAsync($"/api/players/{player.Id}/goals", new[] { new Goal { Stat = "ppg", Target = 5 } })).StatusCode);
    }

    [Theory]
    [InlineData("https://ibasketball.co.il/wp-content/uploads/2026/09/crest-150x150.png", true)]
    [InlineData("http://ibasketball.co.il/wp-content/uploads/2026/09/crest.png", false)]
    [InlineData("https://ibasketball.co.il/wp-json/sportspress/v2/teams", false)]
    [InlineData("https://evil.example/wp-content/uploads/x.png", false)]
    [InlineData("https://ibasketball.co.il.evil.example/wp-content/uploads/x.png", false)]
    [InlineData("https://ibasketball.co.il:8443/wp-content/uploads/x.png", false)]
    [InlineData("not a url", false)]
    public void Only_IBBAs_own_uploads_are_passed_through(string url, bool allowed) =>
        Assert.Equal(allowed, ImagesController.Allowed(url));
}
