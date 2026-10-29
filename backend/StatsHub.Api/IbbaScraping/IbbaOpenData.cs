using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StatsHub.Api.Leagues;
using StatsHub.Api.Services;

namespace StatsHub.Api.IbbaScraping;

/// <summary>
/// IBBA's open data (the site's WordPress / SportsPress API under /wp-json/) -
/// answers in well under a second, where a team or league page IBBA hasn't
/// cached takes 15-25 s to build. Teams (name, crest, the id the games
/// spreadsheet needs, leagues), leagues (name, page) and league tables are
/// open; players are not (their page is still read). Only this season's
/// league is ever asked for - a team belongs to a new league every season,
/// ~25 over the years.
///
/// Every method returns null when the data isn't there or doesn't look as
/// expected, so the caller falls back to reading the page as before.
/// </summary>
public class IbbaOpenData
{
    private const string Api = "https://ibasketball.co.il/wp-json/sportspress/v2";
    private readonly HttpClient _http;

    public IbbaOpenData(HttpClient http) => _http = http;

    public sealed record TeamInfo(int Id, string Name, string TeamUrl, string? CrestUrl, List<int> LeagueIds)
    {
        // The team's games spreadsheet - the same address its page links to.
        public string GamesSpreadsheetUrl => $"{TeamUrl}?feed=xlsx&team_id={Id}";
    }

    public sealed record LeagueInfo(int Id, string Name, string PageUrl);

    /// <summary>A team by its page address (/team/4180-מכבי-חיפה/).</summary>
    public async Task<TeamInfo?> FindTeamAsync(string teamUrl)
    {
        var slug = SlugOf(teamUrl);
        if (slug == null) return null;
        using var timing = RequestTimings.Time("ibba-api-team");
        var teams = await GetAsync<List<TeamJson>>($"{Api}/teams?slug={Uri.EscapeDataString(slug)}&_embed=wp:featuredmedia&_fields=id,title,link,leagues,_links,_embedded");
        var team = teams?.FirstOrDefault();
        return team == null ? null : ToInfo(team);
    }

    /// <summary>
    /// A team known only by the id a game uses for it and its name (a cup
    /// opponent from another league): the name is searched, and the team
    /// whose address starts with that id is the one.
    /// </summary>
    public async Task<TeamInfo?> FindTeamByCodeAsync(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) return null;
        using var timing = RequestTimings.Time("ibba-api-team-by-code");
        var teams = await GetAsync<List<TeamJson>>($"{Api}/teams?search={Uri.EscapeDataString(name)}&per_page=50&_embed=wp:featuredmedia&_fields=id,title,link,leagues,_links,_embedded");
        var team = teams?.FirstOrDefault(t => SlugOf(t.Link ?? "")?.StartsWith(code + "-", StringComparison.Ordinal) == true);
        return team == null ? null : ToInfo(team);
    }

    /// <summary>
    /// This season's league of that name among the team's leagues - the names
    /// repeat every season, and the newest (highest id) is the current one.
    /// </summary>
    public async Task<LeagueInfo?> FindCurrentLeagueAsync(IEnumerable<int> leagueIds, string leagueName)
    {
        var ids = leagueIds.Distinct().ToList();
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(leagueName)) return null;
        using var timing = RequestTimings.Time("ibba-api-league");
        var leagues = await GetAsync<List<LeagueJson>>($"{Api}/leagues?include={string.Join(',', ids)}&per_page=100&_fields=id,name,link");
        var league = leagues?
            .Where(l => Decode(l.Name) == leagueName.Trim() && !string.IsNullOrEmpty(l.Link))
            .MaxBy(l => l.Id);
        return league == null ? null : new LeagueInfo(league.Id, Decode(league.Name), PageUrl(league.Link!));
    }

    /// <summary>
    /// The league's table (its regular-season one when it has stages), each
    /// row with the team's page address - one request for the table, one for
    /// the addresses.
    /// </summary>
    public async Task<List<LeagueStanding>?> GetStandingsAsync(int leagueId)
    {
        using var timing = RequestTimings.Time("ibba-api-standings");
        var tables = await GetAsync<List<TableJson>>($"{Api}/tables?leagues={leagueId}&_fields=id,title,data");
        var table = tables?
            .OrderByDescending(t => Decode(t.Title?.Rendered).Contains("רגילה"))
            .ThenBy(t => t.Id)
            .FirstOrDefault();
        if (table?.Data == null) return null;

        // Columns by the same headers the league page shows (מש׳ ניצ׳ ...).
        var rows = table.Data.Where(r => r.Key != "0").ToList();
        if (rows.Count == 0) return [];
        var teamIds = rows.Select(r => int.TryParse(r.Key, out var id) ? id : 0).Where(id => id > 0).ToList();
        var links = await GetAsync<List<TeamJson>>($"{Api}/teams?include={string.Join(',', teamIds)}&per_page=100&_fields=id,link");
        if (links == null) return null;
        var urlById = links.Where(t => !string.IsNullOrEmpty(t.Link)).ToDictionary(t => t.Id, t => PageUrl(t.Link!));

        var standings = new List<LeagueStanding>();
        foreach (var (key, row) in rows)
        {
            if (!int.TryParse(key, out var id) || !urlById.TryGetValue(id, out var url)) return null;
            int Number(string column) => row.TryGetValue(column, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble())
                : row.TryGetValue(column, out v) && v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var n) ? n
                : throw new LeagueLayoutException($"league table {table.Id} row {key} has no \"{column}\"");
            standings.Add(new LeagueStanding
            {
                Position = Number("pos"),
                TeamName = row.TryGetValue("name", out var name) && name.ValueKind == JsonValueKind.String ? Decode(name.GetString()) : "",
                TeamUrl = url,
                GamesPlayed = Number("gp"),
                Wins = Number("w"),
                Losses = Number("l"),
                Technical = Number("lt"),
                PointsFor = Number("bf"),
                PointsAgainst = Number("ba"),
                Diff = Number("bd"),
                LeaguePoints = Number("pts"),
            });
        }
        return standings.OrderBy(s => s.Position).ToList();
    }

    private async Task<T?> GetAsync<T>(string url) where T : class
    {
        try
        {
            return await _http.GetFromJsonAsync<T>(url);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
        {
            return null;
        }
    }

    private static TeamInfo? ToInfo(TeamJson team)
    {
        if (string.IsNullOrEmpty(team.Link)) return null;
        var media = team.Embedded?.FeaturedMedia?.FirstOrDefault();
        var crest = media?.MediaDetails?.Sizes?.GetValueOrDefault("thumbnail")?.SourceUrl ?? media?.SourceUrl;
        return new TeamInfo(team.Id, Decode(team.Title?.Rendered), PageUrl(team.Link), crest, team.Leagues ?? []);
    }

    // The page address as the rest of the app stores it: readable Hebrew,
    // not percent-encoded (/team/4180-מכבי-חיפה/).
    private static string PageUrl(string link) => Uri.UnescapeDataString(link);

    private static string? SlugOf(string pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri)) return null;
        var slug = Uri.UnescapeDataString(uri.Segments.LastOrDefault()?.Trim('/') ?? "");
        return slug.Length == 0 ? null : slug;
    }

    private static string Decode(string? text) => WebUtility.HtmlDecode(text ?? "").Trim();

    private sealed record RenderedText(string? Rendered);

    private sealed class TeamJson
    {
        public int Id { get; set; }
        public RenderedText? Title { get; set; }
        public string? Link { get; set; }
        public List<int>? Leagues { get; set; }
        [JsonPropertyName("_embedded")] public Embeds? Embedded { get; set; }
    }

    private sealed record Embeds([property: JsonPropertyName("wp:featuredmedia")] List<Media>? FeaturedMedia);
    private sealed record Media(
        [property: JsonPropertyName("source_url")] string? SourceUrl,
        [property: JsonPropertyName("media_details")] MediaDetails? MediaDetails);
    private sealed record MediaDetails(Dictionary<string, MediaSize>? Sizes);
    private sealed record MediaSize([property: JsonPropertyName("source_url")] string? SourceUrl);

    private sealed record LeagueJson(int Id, string? Name, string? Link);

    private sealed class TableJson
    {
        public int Id { get; set; }
        public RenderedText? Title { get; set; }
        public Dictionary<string, Dictionary<string, JsonElement>>? Data { get; set; }
    }
}
