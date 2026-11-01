using System.Net;
using System.Net.Http.Json;
using StatsHub.Api.DTOs;
using StatsHub.Api.Tests.Infrastructure;
using static StatsHub.Api.Controllers.InvitesController;

namespace StatsHub.Api.Tests;

public class FamilyInviteTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public FamilyInviteTests(StatsHubFactory factory) => _factory = factory;

    private static Task<InviteDto> InviteAsync(TestUser parent, string kind, params Guid[] playerIds) =>
        parent.PostAsync<InviteDto>("/api/invites", new CreateInviteDto { Kind = kind, PlayerIds = playerIds.ToList() });

    [Fact]
    public async Task One_parent_link_brings_in_both_kids_and_more_than_one_person()
    {
        var mom = await TestUser.SignInAsync(_factory, "Mom");
        var yoav = await mom.CreatePlayerAsync("Yoav");
        var noa = await mom.CreatePlayerAsync("Noa");
        var invite = await InviteAsync(mom, "Parent", yoav.Id, noa.Id);

        var dad = await TestUser.SignInAsync(_factory, "Dad");
        var grandma = await TestUser.SignInAsync(_factory, "Grandma");
        var accepted = await dad.PostAsync<AcceptResultDto>($"/api/invites/{invite.Code}/accept", new { });
        await grandma.PostAsync<AcceptResultDto>($"/api/invites/{invite.Code.ToLowerInvariant()}/accept", new { });

        Assert.Equal(new[] { noa.Id, yoav.Id }.Order(), accepted.PlayerIds.Order());
        Assert.Equal(new[] { "Noa", "Yoav" }, (await dad.GetAsync<List<PlayerDto>>("/api/players")).Select(p => p.FirstName).Order());
        Assert.Equal(2, (await grandma.GetAsync<List<PlayerDto>>("/api/players")).Count);
        var sent = Assert.Single(await mom.GetAsync<List<InviteDto>>("/api/invites"));
        Assert.Equal(new[] { "Dad", "Grandma" }, sent.Joined);
    }

    [Fact]
    public async Task The_link_page_shows_who_invited_and_the_kids_without_signing_in()
    {
        var mom = await TestUser.SignInAsync(_factory, "Mom");
        var yoav = await mom.CreatePlayerAsync("Yoav");
        var invite = await InviteAsync(mom, "Parent", yoav.Id);

        var anonymous = _factory.CreateClient();
        var preview = (await anonymous.GetFromJsonAsync<InvitePreviewDto>($"/api/invites/{invite.Code}", TestUser.Json))!;

        Assert.Equal(("Parent", "Mom", "open"), (preview.Kind, preview.InvitedBy, preview.Status));
        Assert.Equal("Yoav", Assert.Single(preview.Players).FirstName);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/invites/{invite.Code}/accept", null)).StatusCode);
    }

    [Fact]
    public async Task A_kid_added_later_is_shared_with_the_family()
    {
        var mom = await TestUser.SignInAsync(_factory, "Mom");
        var yoav = await mom.CreatePlayerAsync("Yoav");
        var dad = await TestUser.SignInAsync(_factory, "Dad");
        await dad.PostAsync<AcceptResultDto>($"/api/invites/{(await InviteAsync(mom, "Parent", yoav.Id)).Code}/accept", new { });

        var noa = await mom.CreatePlayerAsync("Noa");

        Assert.Contains(noa.Parents, p => p.UserId == dad.User.Id);
        Assert.Contains(await dad.GetAsync<List<PlayerDto>>("/api/players"), p => p.Id == noa.Id);
    }

    [Fact]
    public async Task A_player_link_is_one_kids_login_and_works_once()
    {
        var mom = await TestUser.SignInAsync(_factory, "Mom");
        var noa = await mom.CreatePlayerAsync("Noa");
        var invite = await InviteAsync(mom, "Player", noa.Id);

        var kid = await TestUser.SignInAsync(_factory, "Noa");
        await kid.PostAsync<AcceptResultDto>($"/api/invites/{invite.Code}/accept", new { });
        await kid.PostAsync<AcceptResultDto>($"/api/invites/{invite.Code}/accept", new { }); // opened again - still fine

        var me = await kid.GetAsync<UserDto>("/api/auth/me");
        Assert.Equal(("Player", noa.Id), (me.Role, me.LinkedPlayer!.Id));
        Assert.Equal(kid.User.Id, (await mom.GetAsync<PlayerDto>($"/api/players/{noa.Id}")).LinkedUser!.UserId);

        var someoneElse = await TestUser.SignInAsync(_factory, "Other");
        var again = await someoneElse.Http.PostAsync($"/api/invites/{invite.Code}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Empty(await mom.GetAsync<List<InviteDto>>("/api/invites"));
    }

    [Fact]
    public async Task A_parent_account_cannot_take_a_kids_link_and_a_kid_cannot_take_a_parents()
    {
        var mom = await TestUser.SignInAsync(_factory, "Mom");
        var noa = await mom.CreatePlayerAsync("Noa");
        var yoav = await mom.CreatePlayerAsync("Yoav");

        var otherParent = await TestUser.SignInAsync(_factory, "OtherParent");
        await otherParent.CreatePlayerAsync("Dan");
        var asParent = await otherParent.Http.PostAsync($"/api/invites/{(await InviteAsync(mom, "Player", noa.Id)).Code}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, asParent.StatusCode);
        Assert.Contains("parent-account", await asParent.Content.ReadAsStringAsync());

        var kid = await TestUser.SignInAsync(_factory, "Yoav");
        await kid.PostAsync<AcceptResultDto>($"/api/invites/{(await InviteAsync(mom, "Player", yoav.Id)).Code}/accept", new { });
        var kidAsParent = await kid.Http.PostAsync($"/api/invites/{(await InviteAsync(mom, "Parent", noa.Id)).Code}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, kidAsParent.StatusCode);
        Assert.Contains("player-account", await kidAsParent.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Only_a_kids_parent_can_invite_for_them()
    {
        var mom = await TestUser.SignInAsync(_factory, "Mom");
        var noa = await mom.CreatePlayerAsync("Noa");
        var stranger = await TestUser.SignInAsync(_factory, "Stranger");

        var response = await stranger.Http.PostAsJsonAsync("/api/invites", new CreateInviteDto { Kind = "Parent", PlayerIds = new() { noa.Id } });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var twoKids = await mom.Http.PostAsJsonAsync("/api/invites", new CreateInviteDto { Kind = "Player", PlayerIds = new() { noa.Id, Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, twoKids.StatusCode);
    }

    [Fact]
    public async Task A_cancelled_link_no_longer_works()
    {
        var mom = await TestUser.SignInAsync(_factory, "Mom");
        var noa = await mom.CreatePlayerAsync("Noa");
        var invite = await InviteAsync(mom, "Parent", noa.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await mom.Http.DeleteAsync($"/api/invites/{invite.Id}")).StatusCode);

        var dad = await TestUser.SignInAsync(_factory, "Dad");
        Assert.Equal(HttpStatusCode.Conflict, (await dad.Http.PostAsync($"/api/invites/{invite.Code}/accept", null)).StatusCode);
        Assert.Equal("expired", (await dad.GetAsync<InvitePreviewDto>($"/api/invites/{invite.Code}")).Status);
    }

    [Fact]
    public async Task A_parent_can_take_someone_off_but_not_themself()
    {
        var mom = await TestUser.SignInAsync(_factory, "Mom");
        var noa = await mom.CreatePlayerAsync("Noa");
        var dad = await TestUser.SignInAsync(_factory, "Dad");
        await dad.PostAsync<AcceptResultDto>($"/api/invites/{(await InviteAsync(mom, "Parent", noa.Id)).Code}/accept", new { });
        var kid = await TestUser.SignInAsync(_factory, "Noa");
        await kid.PostAsync<AcceptResultDto>($"/api/invites/{(await InviteAsync(mom, "Player", noa.Id)).Code}/accept", new { });

        Assert.Equal(HttpStatusCode.NotFound, (await mom.Http.DeleteAsync($"/api/players/{noa.Id}/parents/{mom.User.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mom.Http.DeleteAsync($"/api/players/{noa.Id}/parents/{dad.User.Id}")).StatusCode);
        var after = (await (await mom.Http.DeleteAsync($"/api/players/{noa.Id}/linked-user")).Content.ReadFromJsonAsync<PlayerDto>(TestUser.Json))!;

        Assert.Equal(mom.User.Id, Assert.Single(after.Parents).UserId);
        Assert.Null(after.LinkedUser);
        Assert.Empty(await dad.GetAsync<List<PlayerDto>>("/api/players"));
        Assert.Equal("Parent", (await kid.GetAsync<UserDto>("/api/auth/me")).Role);
    }
}
