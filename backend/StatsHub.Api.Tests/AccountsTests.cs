using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using StatsHub.Api.DTOs;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

public class AccountsTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public AccountsTests(StatsHubFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_then_log_in_with_the_same_password()
    {
        var http = _factory.CreateClient();
        var email = $"pw-{Guid.NewGuid():N}@test.local";

        var register = await http.PostAsJsonAsync("/api/auth/register", new RegisterDto { Email = email, Password = "correct horse battery", FirstName = "Pat", LastName = "Parent" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var login = await http.PostAsJsonAsync("/api/auth/login", new PasswordLoginDto { Email = email, Password = "correct horse battery" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponseDto>(TestUser.Json))!;
        Assert.Equal(email, auth.User.Email);
    }

    [Fact]
    public async Task A_wrong_password_or_unknown_email_is_refused_without_saying_which()
    {
        var http = _factory.CreateClient();
        var email = $"pw-{Guid.NewGuid():N}@test.local";
        await http.PostAsJsonAsync("/api/auth/register", new RegisterDto { Email = email, Password = "right-password", FirstName = "Pat", LastName = "Parent" });

        var wrongPassword = await http.PostAsJsonAsync("/api/auth/login", new PasswordLoginDto { Email = email, Password = "wrong-password" });
        var unknownEmail = await http.PostAsJsonAsync("/api/auth/login", new PasswordLoginDto { Email = $"nobody-{Guid.NewGuid():N}@test.local", Password = "whatever" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(), await unknownEmail.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_same_email_cannot_register_twice()
    {
        var http = _factory.CreateClient();
        var email = $"pw-{Guid.NewGuid():N}@test.local";
        await http.PostAsJsonAsync("/api/auth/register", new RegisterDto { Email = email, Password = "one-password", FirstName = "A", LastName = "B" });

        var again = await http.PostAsJsonAsync("/api/auth/register", new RegisterDto { Email = email.ToUpperInvariant(), Password = "two-password", FirstName = "C", LastName = "D" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Registering_with_an_existing_google_accounts_email_does_not_take_it_over()
    {
        // dev-login creates an account with no password - exactly what a
        // Google sign-up looks like.
        var victim = await TestUser.SignInAsync(_factory, "victim");
        var victimPlayer = await victim.CreatePlayerAsync("Victim's kid");

        var attacker = _factory.CreateClient();
        var register = await attacker.PostAsJsonAsync("/api/auth/register", new RegisterDto { Email = victim.User.Email, Password = "attacker-chosen", FirstName = "Evil", LastName = "Person" });
        Assert.Equal(HttpStatusCode.BadRequest, register.StatusCode);

        var login = await attacker.PostAsJsonAsync("/api/auth/login", new PasswordLoginDto { Email = victim.User.Email, Password = "attacker-chosen" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        // The victim's account is untouched.
        var me = await victim.GetAsync<UserDto>("/api/auth/me");
        Assert.Equal("victim", me.FirstName);
        Assert.Contains(await victim.GetAsync<List<PlayerDto>>("/api/players"), p => p.Id == victimPlayer.Id);
    }

    [Fact]
    public async Task Email_sign_in_ignores_capital_letters()
    {
        var http = _factory.CreateClient();
        var email = $"Mixed.Case-{Guid.NewGuid():N}@Test.Local";
        await http.PostAsJsonAsync("/api/auth/register", new RegisterDto { Email = email, Password = "right-password", FirstName = "Pat", LastName = "Parent" });

        var login = await http.PostAsJsonAsync("/api/auth/login", new PasswordLoginDto { Email = "  " + email.ToLowerInvariant() + " ", Password = "right-password" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task A_player_account_sees_its_own_stats_but_cannot_manage_the_family()
    {
        var parent = await TestUser.SignInAsync(_factory, "parent");
        var player = await parent.CreatePlayerAsync("Kid");
        var team = await parent.CreateTeamWithPlayerAsync(player.Id);
        var game = await parent.CreateGameAsync(team.Id);
        await parent.CreateStatsAsync(game.Id, player.Id, ftm: 3, fta: 3);
        var invite = await parent.PostAsync<PlayerInviteDto>($"/api/players/{player.Id}/invite", new CreatePlayerInviteDto());

        var kid = await TestUser.SignInAsync(_factory, "kid");
        await kid.PostAsync<PlayerDto>("/api/players/claim-invite", new ClaimInviteDto { InviteCode = invite.InviteCode });
        var me = await kid.GetAsync<UserDto>("/api/auth/me");
        Assert.Equal("Player", me.Role);

        var games = await kid.GetAsync<List<GameDto>>($"/api/games/player/{player.Id}");
        Assert.Equal(3, games.Single(g => g.Id == game.Id).PlayerStats.Single().TotalPoints);

        // Read-only: can't delete the player, edit the game, or add a teammate.
        Assert.False((await kid.Http.DeleteAsync($"/api/players/{player.Id}")).IsSuccessStatusCode);
        Assert.False((await kid.Http.PutAsJsonAsync($"/api/games/{game.Id}", new UpdateGameDto { Status = "Completed", TeamScore = 99, OpponentScore = 0 })).IsSuccessStatusCode);
        Assert.False((await kid.Http.PostAsJsonAsync($"/api/players/{player.Id}/parent-invite", new CreateParentInviteDto())).IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var token = parent.Http.DefaultRequestHeaders.Authorization!.Parameter!;
        var tampered = token[..^4] + (token[^4..] == "AAAA" ? "BBBB" : "AAAA");

        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/players")).StatusCode);
    }
}
