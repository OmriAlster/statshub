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
    /// The team's newest league that isn't a cup - the one it plays in this
    /// season (the cup is its own league, named גביע ...).
    /// </summary>
    public async Task<LeagueInfo?> FindNewestLeagueAsync(IEnumerable<int> leagueIds)
    {
        var ids = leagueIds.Distinct().ToList();
        if (ids.Count == 0) return null;
        using var timing = RequestTimings.Time("ibba-api-league");
        var leagues = await GetAsync<List<LeagueJson>>($"{Api}/leagues?include={string.Join(',', ids)}&per_page=100&_fields=id,name,link");
        var league = leagues?
            .Where(l => !Decode(l.Name).Contains("גביע") && !string.IsNullOrEmpty(l.Link))
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
        // A league can have more than one table of the same name - one left
        // empty (everyone on 0 games) beside the live one: the live one is the
        // one with games in it, the newest when two do.
        var table = tables?
            .OrderByDescending(t => Decode(t.Title?.Rendered).Contains("רגילה"))
            .ThenByDescending(t => GamesIn(t) > 0)
            .ThenByDescending(t => t.Id)
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

    private static int GamesIn(TableJson table) =>
        table.Data?.Where(r => r.Key != "0")
            .Sum(r => r.Value.TryGetValue("gp", out var gp) && gp.ValueKind == JsonValueKind.Number ? gp.GetInt32() : 0) ?? 0;

    /// <summary>
    /// This season's cups a team plays in, each as its bracket. A team isn't
    /// always listed under the cup it plays (the open data left this season's
    /// cup off some teams), so every cup of the season is looked at and the
    /// team found inside its bracket (kept half an hour per team - IbbaService).
    /// A cup's bracket is a grid of slots: the
    /// first round's games, then the next round's, and so on to the final
    /// (2^rounds - 1 in all), each slot with its two teams - known before the
    /// game itself is created - and the game once it is (date, score).
    /// </summary>
    public async Task<List<LeagueCup>> GetCupsAsync(string teamUrl)
    {
        var team = await FindTeamAsync(teamUrl);
        if (team == null) return [];
        using var timing = RequestTimings.Time("ibba-api-cups");

        var cups = new List<LeagueCup>();
        foreach (var tournament in await SeasonCupsAsync())
        {
            var inIt = tournament.Rows.SelectMany(Cells).Any(c =>
                c.TryGetValue("type", out var type) && type.GetString() == "team" && c.TryGetValue("id", out var id) && IntOf(id) == team.Id);
            if (inIt) cups.Add(await ToCupAsync(tournament));
        }
        return cups;
    }

    // Every cup of the current season (the newest season with a name like 2026-2027).
    private async Task<List<TournamentJson>> SeasonCupsAsync()
    {
        var seasons = await GetAsync<List<LeagueJson>>($"{Api}/seasons?per_page=10&orderby=id&order=desc&_fields=id,name") ?? [];
        var current = seasons.FirstOrDefault(s => System.Text.RegularExpressions.Regex.IsMatch(Decode(s.Name), @"^\d{4}-\d{4}$"));
        if (current == null) return [];
        return await GetAsync<List<TournamentJson>>($"{Api}/tournaments?seasons={current.Id}&per_page=100&_fields=id,title,link,rounds,data") ?? [];
    }

    private async Task<LeagueCup> ToCupAsync(TournamentJson tournament)
    {
        var cup = new LeagueCup { Name = Decode(tournament.Title?.Rendered), PageUrl = PageUrl(tournament.Link ?? "") };
        var rounds = tournament.Rounds;
        if (rounds < 2) return cup; // not drawn yet

        // Each slot's teams (in the order shown: home first) and its game.
        var slotTeams = new Dictionary<int, List<int>>();
        var slotEvents = new Dictionary<int, int>();
        foreach (var row in tournament.Rows)
        {
            foreach (var cell in Cells(row))
            {
                if (!cell.TryGetValue("type", out var type) || !cell.TryGetValue("index", out var indexValue)) continue;
                var index = IntOf(indexValue);
                var id = cell.TryGetValue("id", out var idValue) ? IntOf(idValue) : 0;
                if (type.GetString() == "team")
                {
                    if (!slotTeams.TryGetValue(index, out var ids)) slotTeams[index] = ids = [];
                    ids.Add(id);
                }
                else if (type.GetString() == "event" && id > 0) slotEvents[index] = id;
            }
        }

        var teamIds = slotTeams.Values.SelectMany(t => t).Where(id => id > 0).Distinct().ToList();
        var teams = teamIds.Count == 0 ? [] : await GetAsync<List<TeamJson>>($"{Api}/teams?include={string.Join(',', teamIds)}&per_page=100&_embed=wp:featuredmedia&_fields=id,title,link,_links,_embedded") ?? [];
        var teamById = teams.Select(ToInfo).OfType<TeamInfo>().ToDictionary(t => t.Id);
        var events = slotEvents.Count == 0 ? [] : await GetAsync<List<EventJson>>($"{Api}/events?include={string.Join(',', slotEvents.Values)}&per_page=100&_fields=id,date,teams,main_results") ?? [];
        var eventById = events.ToDictionary(e => e.Id);

        var offset = 0;
        for (var round = 0; round < rounds; round++)
        {
            var games = 1 << (rounds - 1 - round);
            var cupRound = new LeagueCupRound { Name = RoundName(games) };
            for (var slot = offset; slot < offset + games; slot++)
            {
                var sides = slotTeams.GetValueOrDefault(slot) ?? [];
                eventById.TryGetValue(slotEvents.GetValueOrDefault(slot), out var game);
                // The game's own order of teams (and its scores) when it exists.
                var order = game?.Teams is { Count: 2 } t ? t : sides;
                var scores = game?.MainResults ?? [];
                LeagueCupSide? Side(int i)
                {
                    var id = i < order.Count ? order[i] : 0;
                    if (!teamById.TryGetValue(id, out var info)) return null;
                    return new LeagueCupSide { Name = info.Name, TeamUrl = info.TeamUrl, LogoUrl = info.CrestUrl, Score = i < scores.Count ? IntOrNull(scores[i]) : null };
                }
                var (home, away) = (Side(0), Side(1));
                if (home?.Score is int h && away?.Score is int a && h != a) (h > a ? home : away)!.Won = true;
                cupRound.Games.Add(new LeagueCupGame
                {
                    Home = home,
                    Away = away,
                    LocalDate = game?.Date is string d && DateTime.TryParse(d, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : null,
                });
            }
            cup.Rounds.Add(cupRound);
            offset += games;
        }
        return cup;
    }

    /// <summary>
    /// The players' lines of a game, by player id - across both teams. A
    /// game's address is its code (/match/781683/). Empty when the league
    /// published none (youth leagues), null when the game isn't there.
    /// viaPage: when the API's list leaves the game out, find it through its
    /// page (slower - asked only once the league is known to publish lines).
    /// </summary>
    public async Task<Dictionary<string, Dictionary<string, JsonElement>>?> GetGameLinesAsync(string gameCode, bool viaPage = true)
    {
        using var timing = RequestTimings.Time("ibba-api-game");
        var games = await GetAsync<List<GameLinesJson>>($"{Api}/events?slug={Uri.EscapeDataString(gameCode)}&_fields=id,performance");
        // The API's game lists leave some finished games out (a team's first
        // weeks - seen October 2026) that it still serves by id; the game's
        // page names its id (postid-NNNN).
        var game = games?.FirstOrDefault() ?? (viaPage ? await GameByPageAsync(gameCode) : null);
        if (game == null) return null;

        var lines = new Dictionary<string, Dictionary<string, JsonElement>>();
        if (game.Performance.ValueKind != JsonValueKind.Object) return lines;
        foreach (var team in game.Performance.EnumerateObject())
        {
            if (team.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var player in team.Value.EnumerateObject())
            {
                // "0" is the team's totals; a player has points or minutes on record.
                if (player.Name == "0" || player.Value.ValueKind != JsonValueKind.Object) continue;
                var line = player.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                if (line.ContainsKey("pts")) lines[player.Name] = line;
            }
        }
        return lines;
    }

    // A game's id never changes - its page is read once.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> EventIdByCode = new();

    private async Task<GameLinesJson?> GameByPageAsync(string gameCode)
    {
        if (!EventIdByCode.TryGetValue(gameCode, out var eventId))
        {
            string page;
            try { page = await _http.GetStringAsync($"https://ibasketball.co.il/match/{Uri.EscapeDataString(gameCode)}/"); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
            var id = System.Text.RegularExpressions.Regex.Match(page, @"postid-(\d+)");
            if (!id.Success) return null;
            eventId = EventIdByCode[gameCode] = id.Groups[1].Value;
        }
        return await GetAsync<GameLinesJson>($"{Api}/events/{eventId}?_fields=id,performance");
    }

    // The league's columns: fgm/fga are two-pointers (pts = 2*fgm + 3*threepm + ftm).
    public static LeagueBoxScore ToBoxScore(Dictionary<string, JsonElement> line)
    {
        int N(string key) => line.TryGetValue(key, out var v) ? IntOrNull(v) ?? 0 : 0;
        var minutes = line.TryGetValue("min", out var min) && min.ValueKind == JsonValueKind.String ? MinutesOf(min.GetString()) : N("mins");
        return new LeagueBoxScore
        {
            TwoPointersMade = N("fgm"),
            TwoPointersAttempted = N("fga"),
            ThreePointersMade = N("threepm"),
            ThreePointersAttempted = N("threepa"),
            FreeThrowsMade = N("ftm"),
            FreeThrowsAttempted = N("fta"),
            OffensiveRebounds = N("off"),
            DefensiveRebounds = N("def"),
            Assists = N("ast"),
            Steals = N("stl"),
            Blocks = N("blk"),
            Turnovers = N("to"),
            Fouls = N("pf"),
            Minutes = minutes,
        };
    }

    private sealed class GameLinesJson
    {
        public int Id { get; set; }
        public JsonElement Performance { get; set; }
    }

    private static string RoundName(int games) => games switch
    {
        1 => "Final",
        2 => "Semi-finals",
        4 => "Quarter-finals",
        _ => $"Round of {games * 2}",
    };

    // A bracket row is a list or an object of its cells (the site's grid, either shape).
    private static IEnumerable<Dictionary<string, JsonElement>> Cells(JsonElement row)
    {
        var cells = row.ValueKind == JsonValueKind.Array ? row.EnumerateArray()
            : row.ValueKind == JsonValueKind.Object ? row.EnumerateObject().Select(p => p.Value) : [];
        foreach (var cell in cells)
            if (cell.ValueKind == JsonValueKind.Object)
                yield return cell.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
    }

    // Time played as the league writes it - "00:30:47" (h:m:s) or "30:47" -
    // in whole minutes, rounded.
    private static int MinutesOf(string? text)
    {
        var parts = (text ?? "").Split(':');
        if (parts.Length < 2 || parts.Any(p => !int.TryParse(p, out _))) return 0;
        var n = parts.Select(int.Parse).ToArray();
        var (h, m, s) = n.Length == 3 ? (n[0], n[1], n[2]) : (0, n[0], n[1]);
        return h * 60 + m + (s >= 30 ? 1 : 0);
    }

    private static int IntOf(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number ? value.GetInt32()
        : value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var n) ? n : 0;

    private static int? IntOrNull(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number ? value.GetInt32()
        : value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var n) ? n : null;

    private sealed class TournamentJson
    {
        public int Id { get; set; }
        public RenderedText? Title { get; set; }
        public string? Link { get; set; }
        [JsonPropertyName("rounds")] public JsonElement RoundsValue { get; set; } // a number, or the same as text
        [JsonIgnore] public int Rounds => IntOf(RoundsValue);
        // The bracket's rows - a list, or an object of rows by number (both seen).
        public JsonElement Data { get; set; }

        [JsonIgnore] public IEnumerable<JsonElement> Rows => Data.ValueKind switch
        {
            JsonValueKind.Array => Data.EnumerateArray(),
            JsonValueKind.Object => Data.EnumerateObject().Select(p => p.Value),
            _ => [],
        };
    }

    private sealed class EventJson
    {
        public int Id { get; set; }
        public string? Date { get; set; }
        public List<int>? Teams { get; set; }
        [JsonPropertyName("main_results")] public List<JsonElement>? MainResults { get; set; }
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
