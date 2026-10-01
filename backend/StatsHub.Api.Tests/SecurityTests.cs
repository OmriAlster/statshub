using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StatsHub.Api.DTOs;
using StatsHub.Api.Services;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

// Passwords, sessions, attempt limits and the Google account-takeover guard.
public class SecurityTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public SecurityTests(StatsHubFactory factory) => _factory = factory;

    private static string NewEmail() => $"sec-{Guid.NewGuid():N}@test.local";

    private async Task<HttpResponseMessage> RegisterAsync(HttpClient http, string email, string password) =>
        await http.PostAsJsonAsync("/api/auth/register", new RegisterDto { Email = email, Password = password, FirstName = "Sec", LastName = "Test" });

    [Theory]
    [InlineData("short1!")]          // under 10 characters
    [InlineData("1234567890")]       // a sequence
    [InlineData("abcabcabcabc")]     // a repeated block
    [InlineData("password123")]      // common
    [InlineData("basketball1")]      // common
    public async Task Weak_passwords_are_refused(string password)
    {
        var response = await RegisterAsync(_factory.CreateClient(), NewEmail(), password);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_long_uncommon_password_is_accepted_and_signs_in()
    {
        var http = _factory.CreateClient();
        var email = NewEmail();
        Assert.True((await RegisterAsync(http, email, "Corner three at the buzzer")).IsSuccessStatusCode);
        var login = await http.PostAsJsonAsync("/api/auth/login", new PasswordLoginDto { Email = email, Password = "Corner three at the buzzer" });
        Assert.True(login.IsSuccessStatusCode);
    }

    [Fact]
    public void Old_password_hashes_still_verify_and_are_flagged_for_upgrade()
    {
        // The original "<salt>.<hash>" form at 100,000 iterations.
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("Corner three!", salt, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        var legacy = $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";

        Assert.True(PasswordHasher.Verify("Corner three!", legacy));
        Assert.False(PasswordHasher.Verify("wrong password", legacy));
        Assert.True(PasswordHasher.NeedsRehash(legacy));

        var fresh = PasswordHasher.Hash("Corner three!");
        Assert.StartsWith($"pbkdf2-sha256${PasswordHasher.Iterations}$", fresh);
        Assert.False(PasswordHasher.NeedsRehash(fresh));
    }

    [Fact]
    public async Task Signing_in_upgrades_an_old_password_hash()
    {
        var http = _factory.CreateClient();
        var email = NewEmail();
        Assert.True((await RegisterAsync(http, email, "Corner three at the buzzer")).IsSuccessStatusCode);
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("Corner three at the buzzer", salt, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        await _factory.WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Email == email)).PasswordHash = $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
            await db.SaveChangesAsync();
        });

        var login = await http.PostAsJsonAsync("/api/auth/login", new PasswordLoginDto { Email = email, Password = "Corner three at the buzzer" });

        Assert.True(login.IsSuccessStatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.StartsWith("pbkdf2-sha256$", (await db.Users.SingleAsync(u => u.Email == email)).PasswordHash));
    }

    [Fact]
    public async Task Log_out_everywhere_ends_every_other_session()
    {
        var http = _factory.CreateClient();
        var email = NewEmail();
        var first = (await (await RegisterAsync(http, email, "Corner three at the buzzer")).Content.ReadFromJsonAsync<AuthResponseDto>(TestUser.Json))!;
        var second = (await (await http.PostAsJsonAsync("/api/auth/login", new PasswordLoginDto { Email = email, Password = "Corner three at the buzzer" })).Content.ReadFromJsonAsync<AuthResponseDto>(TestUser.Json))!;

        HttpClient With(string token)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }
        Assert.True((await With(second.Token).GetAsync("/api/auth/me")).IsSuccessStatusCode);

        var response = await With(first.Token).PostAsync("/api/auth/logout-all", null);
        var renewed = (await response.Content.ReadFromJsonAsync<AuthResponseDto>(TestUser.Json))!;

        Assert.Equal(HttpStatusCode.Unauthorized, (await With(first.Token).GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await With(second.Token).GetAsync("/api/auth/me")).StatusCode);
        Assert.True((await With(renewed.Token).GetAsync("/api/auth/me")).IsSuccessStatusCode); // this device stays signed in
    }

    [Fact]
    public async Task Google_sign_in_removes_a_password_set_by_someone_who_never_proved_the_email()
    {
        // Someone registers a parent's email with their own password...
        var http = _factory.CreateClient();
        var email = NewEmail();
        var attacker = (await (await RegisterAsync(http, email, "I set this one myself")).Content.ReadFromJsonAsync<AuthResponseDto>(TestUser.Json))!;

        // ...then the real parent signs in with Google (which proves the email).
        using (var scope = _factory.Services.CreateScope())
        {
            var auth = (AuthService)scope.ServiceProvider.GetRequiredService<IAuthService>();
            var user = await auth.FindOrCreateGoogleUserAsync("google-subject-" + Guid.NewGuid(), email, emailVerified: true, "Real", "Parent", null);
            Assert.Null(user.PasswordHash);
        }

        // The password no longer works, and the session it opened has ended.
        var login = await http.PostAsJsonAsync("/api/auth/login", new PasswordLoginDto { Email = email, Password = "I set this one myself" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        var old = _factory.CreateClient();
        old.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", attacker.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task An_unverified_google_email_is_not_connected_to_an_existing_account()
    {
        var email = NewEmail();
        Assert.True((await RegisterAsync(_factory.CreateClient(), email, "Corner three at the buzzer")).IsSuccessStatusCode);

        using var scope = _factory.Services.CreateScope();
        var auth = (AuthService)scope.ServiceProvider.GetRequiredService<IAuthService>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            auth.FindOrCreateGoogleUserAsync("google-subject-" + Guid.NewGuid(), email, emailVerified: false, "Someone", "Else", null));
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var response = await _factory.CreateClient().GetAsync("/api/auth/me");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }
}

// The sign-in attempt limit, on a server of its own with a low limit.
public class SignInLimitTests : IClassFixture<SignInLimitTests.LowLimitFactory>
{
    public class LowLimitFactory : StatsHubFactory
    {
        public LowLimitFactory() { RateLimitOverride = "3"; }
    }

    private readonly LowLimitFactory _factory;
    public SignInLimitTests(LowLimitFactory factory) => _factory = factory;

    [Fact]
    public async Task Too_many_sign_in_attempts_are_refused_for_a_while()
    {
        var http = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var response = await http.PostAsJsonAsync("/api/auth/login", new PasswordLoginDto { Email = "nobody@test.local", Password = "Guess number " + i });
            statuses.Add(response.StatusCode);
        }
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
    }
}
