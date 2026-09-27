using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StatsHub.Api.DTOs;

namespace StatsHub.Api.Tests.Infrastructure;

// One signed-in account (a parent) talking to the API over HTTP, plus the
// setup steps most tests need. Every test uses fresh accounts with unique
// emails, so tests never see each other's data even on a shared database.
public class TestUser
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public HttpClient Http { get; }
    public UserDto User { get; }

    private TestUser(HttpClient http, UserDto user)
    {
        Http = http;
        User = user;
    }

    public static async Task<TestUser> SignInAsync(StatsHubFactory factory, string? name = null)
    {
        var http = factory.CreateClient();
        var email = $"{name ?? "parent"}-{Guid.NewGuid():N}@test.local";
        var response = await http.PostAsJsonAsync("/api/auth/dev-login", new DevLoginDto { Email = email, FirstName = name ?? "Test", LastName = "Parent" });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponseDto>(Json))!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return new TestUser(http, auth.User);
    }

    public async Task<T> GetAsync<T>(string url)
    {
        var response = await Http.GetAsync(url);
        await EnsureOk(response, url);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Http.PostAsJsonAsync(url, body);
        await EnsureOk(response, url);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task<T> PutAsync<T>(string url, object body)
    {
        var response = await Http.PutAsJsonAsync(url, body);
        await EnsureOk(response, url);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task EnsureOk(HttpResponseMessage response, string url)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{response.RequestMessage?.Method} {url} -> {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}", null, response.StatusCode);
    }

    // ---- Common setup ----

    public Task<PlayerDto> CreatePlayerAsync(string firstName = "Kid") =>
        PostAsync<PlayerDto>("/api/players", new CreatePlayerDto { FirstName = firstName, LastName = "Test", Position = "PG", DateOfBirth = new DateTime(2012, 1, 1) });

    public async Task<TeamDto> CreateTeamWithPlayerAsync(int playerId, string name = "Team", int? jersey = null)
    {
        var team = await PostAsync<TeamDto>("/api/teams", new CreateTeamDto { Name = name });
        var add = await Http.PostAsJsonAsync($"/api/teams/{team.Id}/players/{playerId}", new AddPlayerToTeamDto { JerseyNumber = jersey });
        Assert.Equal(HttpStatusCode.NoContent, add.StatusCode);
        return team;
    }

    public Task<GameDto> CreateGameAsync(int teamId, string type = "League", DateTime? date = null, string opponent = "Rivals") =>
        PostAsync<GameDto>("/api/games", new CreateGameDto { TeamId = teamId, GameType = type, OpponentName = opponent, GameDate = date ?? DateTime.UtcNow.AddDays(-1), Location = "Gym" });

    public Task<GameStatsDto> CreateStatsAsync(int gameId, int playerId, int ftm = 0, int fta = 0, int reb = 0, int ast = 0, int minutes = 20) =>
        PostAsync<GameStatsDto>("/api/gamestats", new CreateGameStatsDto
        {
            GameId = gameId,
            PlayerId = playerId,
            FreeThrowsMade = ftm,
            FreeThrowsAttempted = fta,
            DefensiveRebounds = reb,
            Assists = ast,
            MinutesPlayed = minutes,
        });

    public Task<GameDto> FinishGameAsync(int gameId, int teamScore, int opponentScore) =>
        PutAsync<GameDto>($"/api/games/{gameId}", new UpdateGameDto { Status = "Completed", TeamScore = teamScore, OpponentScore = opponentScore });

    // A second parent with full access to the same player (the parent-invite flow).
    public async Task<TestUser> InviteCoParentAsync(StatsHubFactory factory, int playerId)
    {
        var invite = await PostAsync<ParentInviteDto>($"/api/players/{playerId}/parent-invite", new CreateParentInviteDto());
        var coParent = await SignInAsync(factory, "coparent");
        await coParent.PostAsync<PlayerDto>("/api/players/claim-parent-invite", new ClaimParentInviteDto { InviteCode = invite.InviteCode });
        return coParent;
    }
}
