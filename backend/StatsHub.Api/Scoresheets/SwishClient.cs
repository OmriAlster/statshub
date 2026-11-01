using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatsHub.Api.Scoresheets;

// Swish (api.swish.nbn23.com) - the league's digital scoresheets. Leagues ("competitions")
// each have groups; a group's calendar lists its games; a finished game's scoresheet PDF
// address comes only from /match. The key is read from configuration (Swish:ApiKey, or the
// SWISH_API_KEY environment variable) and never logged.
public sealed class SwishClient
{
    public const string HttpClientName = "Swish";
    private const string Api = "https://api.swish.nbn23.com/";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CompetitionsFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan CalendarFor = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _http;
    private readonly string? _key;
    private readonly ILogger<SwishClient> _log;

    private (DateTime At, List<SwishCompetition> Items)? _competitions;
    private readonly ConcurrentDictionary<string, (DateTime At, SwishCalendar Calendar)> _calendars = new();
    // A finished game's sheet doesn't change: kept a day once there is one (what's read from
    // it is saved anyway). "No sheet yet" isn't kept - it's asked again until one comes.
    private readonly ConcurrentDictionary<string, (DateTime At, ParsedScoresheet Sheet)> _sheets = new();
    private static readonly TimeSpan SheetFor = TimeSpan.FromDays(1);

    public SwishClient(IHttpClientFactory http, IConfiguration config, ILogger<SwishClient> log)
    {
        _http = http;
        _key = config["Swish:ApiKey"] ?? config["SWISH_API_KEY"];
        _log = log;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_key);

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Api + path);
        request.Headers.TryAddWithoutValidation("Authorization", _key);
        using var response = await _http.CreateClient(HttpClientName).SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    public async Task<List<SwishCompetition>> CompetitionsAsync(CancellationToken ct)
    {
        if (_competitions is { } cached && DateTime.UtcNow - cached.At < CompetitionsFor) return cached.Items;
        var items = await GetAsync<List<SwishCompetition>>("competitions", ct);
        _competitions = (DateTime.UtcNow, items);
        return items;
    }

    public async Task<SwishCalendar> CalendarAsync(string groupId, CancellationToken ct)
    {
        if (_calendars.TryGetValue(groupId, out var cached) && DateTime.UtcNow - cached.At < CalendarFor) return cached.Calendar;
        var calendar = await GetAsync<SwishCalendar>("calendar?groupId=" + Uri.EscapeDataString(groupId), ct);
        _calendars[groupId] = (DateTime.UtcNow, calendar);
        return calendar;
    }

    // The game's scoresheet, read - null when the game has none.
    public async Task<ParsedScoresheet?> ScoresheetAsync(string matchId, CancellationToken ct)
    {
        if (_sheets.TryGetValue(matchId, out var known) && DateTime.UtcNow - known.At < SheetFor) return known.Sheet;
        foreach (var stale in _sheets.Where(e => DateTime.UtcNow - e.Value.At >= SheetFor).Select(e => e.Key).ToList())
            _sheets.TryRemove(stale, out _);
        var match = await GetAsync<SwishMatch>("match?id=" + Uri.EscapeDataString(matchId), ct);
        ParsedScoresheet? sheet = null;
        if (!string.IsNullOrWhiteSpace(match.ScoresheetUrl))
        {
            var pdf = await _http.CreateClient(HttpClientName).GetByteArrayAsync(match.ScoresheetUrl, ct);
            try
            {
                sheet = ScoresheetParser.Parse(pdf);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not read the scoresheet of Swish match {MatchId}", matchId);
            }
        }
        if (sheet != null) _sheets[matchId] = (DateTime.UtcNow, sheet);
        return sheet;
    }
}

public sealed record SwishCompetition(string Id, string Name, List<SwishGroup>? Groups);
public sealed record SwishGroup(string Id, string Name);
public sealed record SwishCalendar(string Id, List<SwishRound>? Rounds);
public sealed record SwishRound(string? Name, List<SwishGame>? Matches);
public sealed record SwishTeam(string Id, string Name);
public sealed record SwishTotal(string TeamId, int Total);
public sealed record SwishScore(List<SwishTotal>? Totals);
public sealed record SwishGame(string Id, SwishTeam HomeTeam, SwishTeam AwayTeam, DateTime Date, string Status, SwishScore? Score);
public sealed record SwishMatch(string Id, [property: JsonPropertyName("scoresheetUrl")] string? ScoresheetUrl);
