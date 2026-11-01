using System.Net.Http.Json;
using HtmlAgilityPack;
using StatsHub.Api.Leagues;
using StatsHub.Api.Services;

namespace StatsHub.Api.IbbaScraping;

/// <summary>
/// The Israel Basketball Association (ibasketball.co.il). Player -> every
/// current team (main + any רשאי), and per team - name, crest, league, games
/// (the team's games spreadsheet) and the league table. Reads the site only;
/// saving is the shared sync's.
///
/// Where IBBA's open data has it (IbbaOpenData - teams, leagues, tables) it's
/// read from there, in well under a second; the player's page (players
/// aren't open) and the games spreadsheet are read as files. Should the open
/// data not answer or look different, the team / league page is read
/// instead, as before (IbbaTeamScraper, IbbaLeagueScraper).
/// </summary>
public class IbbaLeagueProvider : ILeagueProvider
{
    private readonly IHttpClientFactory _httpClientFactory;

    public IbbaLeagueProvider(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

    public string Key => "ibba";
    public string Name => "IBBA";
    public string LogoUrl => "/icons/ibba-logo.png";
    public string PlayerUrlExample => "https://ibasketball.co.il/player/…";

    public bool IsPlayerUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Host.EndsWith("ibasketball.co.il", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith("/player/", StringComparison.OrdinalIgnoreCase);

    // Through the "Ibba" client: retries, a request limit, cache rules (Program.cs).
    private HttpClient Http() => _httpClientFactory.CreateClient("Ibba");

    private IbbaOpenData OpenData() => new(Http());

    // What stage 1 keeps of a team for stage 2: its open data, or its page.
    private sealed record PageState(IbbaOpenData.TeamInfo? Info, HtmlDocument? Doc, string? ExcelUrl);

    public Task<LeaguePlayer> GetPlayerAsync(string playerUrl) => new IbbaPlayerScraper(Http()).GetPlayerInfoAsync(playerUrl);

    // prefetchedPlayer lets a caller that already fetched the player's own
    // page (e.g. previewing it before creating the player) hand that result
    // straight in, instead of fetching it again a moment later.
    //
    // Two stages, so a link can answer as soon as the teams are known: this
    // (player page + each team's own page - quick, usually cached by IBBA),
    // then LoadGamesAndStandingsAsync (the games spreadsheet, always fetched
    // fresh and IBBA's slowest page, then the league table).
    public async Task<(LeaguePlayer Player, List<LeagueTeamPage> Teams)> LoadTeamsAsync(string playerUrl, LeaguePlayer? prefetchedPlayer = null)
    {
        var player = prefetchedPlayer ?? await GetPlayerAsync(playerUrl);
        if (player.Teams.Count == 0)
            throw new InvalidOperationException("Could not find any current team for this player on the page.");

        var teamScraper = new IbbaTeamScraper(Http());
        var openData = OpenData();
        var pages = (await Task.WhenAll(player.Teams.Select(team => LoadTeamPageAsync(openData, teamScraper, team)))).ToList();
        return (player, pages);
    }

    private static async Task<LeagueTeamPage> LoadTeamPageAsync(IbbaOpenData openData, IbbaTeamScraper teamScraper, LeagueTeam team)
    {
        // The open data has the name, crest and the id the games spreadsheet
        // needs - no team page at all.
        var info = await openData.FindTeamAsync(team.TeamUrl);
        if (info != null)
        {
            if (!string.IsNullOrEmpty(info.Name)) team.TeamName = info.Name;
            team.TeamLogoUrl = info.CrestUrl ?? "";
            return new LeagueTeamPage { Team = team, State = new PageState(info, null, info.GamesSpreadsheetUrl) };
        }

        // One fetch of the team's own page covers name, crest, and the Excel
        // export link (and later the league link).
        var doc = await teamScraper.LoadTeamPageAsync(team.TeamUrl);

        using (RequestTimings.Time("find-name-crest-export-link"))
        {
            // The team's own page is the source of truth for its name - the player
            // page's link text isn't always just the plain name (רשאי links append
            // " - LeagueName"), which would break exact-match lookups later.
            team.TeamName = teamScraper.GetTeamName(doc) ?? team.TeamName;
            team.TeamLogoUrl = teamScraper.FindTeamLogo(doc, team.TeamUrl, team.TeamName) ?? "";
            return new LeagueTeamPage { Team = team, State = new PageState(null, doc, teamScraper.FindExcelExportUrl(doc, team.TeamUrl)) };
        }
    }

    // A team's games (its Excel export) and, from those, its league and the
    // league's standings.
    public async Task<LeagueTeamReport> LoadGamesAndStandingsAsync(LeagueTeamPage page)
    {
        using var whole = RequestTimings.Time("team-games-and-standings");
        var state = (PageState)page.State!;
        var team = page.Team;
        var report = new LeagueTeamReport { Team = team };
        var teamScraper = new IbbaTeamScraper(Http());

        // The table is fetched alongside the games (IBBA's two slow requests at
        // once), for the team's newest league - kept once the games confirm
        // it's the league they're in.
        var early = state.Info != null ? NewestLeagueStandingsAsync(OpenData(), state.Info.LeagueIds) : null;
        if (state.ExcelUrl != null)
            report.Games = await teamScraper.DownloadAndParseGamesAsync(state.ExcelUrl);

        // Resolve the league from the regular-season (non-cup) games just parsed -
        // the team page's league directory lets us find ANY team's league this way,
        // not just the main team's (unlike the player page's "ליגה" label, which only
        // labels the main team).
        var leagueName = report.Games.FirstOrDefault(g => !g.IsCup)?.League;
        if (string.IsNullOrEmpty(leagueName)) return report;
        team.LeagueName = leagueName;

        if (early != null && await early is { } guessed && guessed.League.Name == leagueName.Trim())
        {
            team.LeagueUrl = guessed.League.PageUrl;
            report.Standings = guessed.Standings;
            return report;
        }

        // This season's league of that name, and its table, from the open data.
        if (state.Info != null)
        {
            var openData = OpenData();
            var league = await openData.FindCurrentLeagueAsync(state.Info.LeagueIds, leagueName);
            if (league != null)
            {
                team.LeagueUrl = league.PageUrl;
                var standings = await openData.GetStandingsAsync(league.Id);
                report.Standings = standings ?? await new IbbaLeagueScraper(Http()).GetStandingsAsync(team.LeagueUrl);
                return report;
            }
        }

        // Otherwise the team page's league directory and the league page.
        var doc = state.Doc ?? await teamScraper.LoadTeamPageAsync(team.TeamUrl);
        team.LeagueUrl = teamScraper.FindLeagueUrl(doc, team.TeamUrl, leagueName) ?? "";
        if (!string.IsNullOrEmpty(team.LeagueUrl))
            report.Standings = await new IbbaLeagueScraper(Http()).GetStandingsAsync(team.LeagueUrl);

        return report;
    }

    // The newest league's table, or null - any failure is left to the
    // ordinary lookup after the games, which reports it properly.
    private static async Task<(IbbaOpenData.LeagueInfo League, List<LeagueStanding> Standings)?> NewestLeagueStandingsAsync(IbbaOpenData openData, IEnumerable<int> leagueIds)
    {
        try
        {
            var league = await openData.FindNewestLeagueAsync(leagueIds);
            if (league == null) return null;
            var standings = await openData.GetStandingsAsync(league.Id);
            return standings == null ? null : (league, standings);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<List<LeagueGame>> LoadTeamGamesAsync(string teamUrl)
    {
        var scraper = new IbbaTeamScraper(Http());
        var info = await OpenData().FindTeamAsync(teamUrl);
        if (info != null) return await scraper.DownloadAndParseGamesAsync(info.GamesSpreadsheetUrl);

        var doc = await scraper.LoadTeamPageAsync(teamUrl);
        var excelUrl = scraper.FindExcelExportUrl(doc, teamUrl)
            ?? throw new LeagueLayoutException($"team page {teamUrl} has no games spreadsheet link");
        return await scraper.DownloadAndParseGamesAsync(excelUrl);
    }

    public async Task<(string CanonicalUrl, string Name, string? LogoUrl)?> ResolveTeamByIdAsync(string teamId, string? teamName = null)
    {
        if (!string.IsNullOrWhiteSpace(teamName) && await OpenData().FindTeamByCodeAsync(teamId, teamName) is { } info)
            return (info.TeamUrl, info.Name, info.CrestUrl);
        return await new IbbaTeamScraper(Http()).ResolveTeamByIdAsync(teamId);
    }

    public async Task<string?> FindTeamLogoUrlAsync(string teamUrl, string teamName)
    {
        // A team the open data knows but has no crest for has none on its page either.
        var info = await OpenData().FindTeamAsync(teamUrl);
        if (info != null) return info.CrestUrl;
        return await new IbbaTeamScraper(Http()).FindTeamLogoUrlAsync(teamUrl, teamName);
    }

    public string TeamIdFromUrl(string teamUrl) => IbbaPlayerScraper.ExtractTeamSlugId(teamUrl);

    // How many name matches a search returns, and how many it asks the site
    // for (past players are dropped from those first).
    internal const int MaxSearchResults = 8;
    internal const int SearchHitsRequested = 30;

    // The site's own search (WordPress's, limited to player pages): names and
    // page addresses only - each page is read separately, as the parent's
    // list fills in, since a page IBBA hasn't cached takes ~3 s. Players not
    // registered this season are left out up front, by their address (below).
    public async Task<List<LeaguePlayerHit>> SearchPlayersAsync(string name)
    {
        var url = $"https://ibasketball.co.il/wp-json/wp/v2/search?search={Uri.EscapeDataString(name)}&type=post&subtype=sp_player&per_page={SearchHitsRequested}";
        var hits = await Http().GetFromJsonAsync<List<SearchHit>>(url) ?? [];
        var now = DateTime.UtcNow;
        return hits
            .Where(h => h.Url != null && IsPlayerUrl(h.Url) && RegisteredThisSeason(h.Url, now))
            .Take(MaxSearchResults)
            .Select(h => new LeaguePlayerHit(HtmlEntity.DeEntitize(h.Title ?? "").Trim(), h.Url!))
            .ToList();
    }

    private sealed record SearchHit(string? Title, string? Url);

    // A player's address carries the time their registration was last saved:
    // /player/246acb577fbe7bd2-30377483/ - the 8 hex digits after the first two
    // are a Unix time (here 6acb577f = Oct 2026). Checked against 80 players:
    // everyone saved since the season began (August) has a current team, and
    // no one saved before it - or with an address in the old format
    // (/player/39-name/) - does. So those are dropped without reading their page.
    internal static bool RegisteredThisSeason(string playerUrl, DateTime now)
    {
        var slug = Uri.TryCreate(playerUrl, UriKind.Absolute, out var uri) ? uri.Segments.LastOrDefault()?.Trim('/') ?? "" : "";
        var match = System.Text.RegularExpressions.Regex.Match(slug, "^[0-9]{2}([0-9a-f]{8})[0-9a-f]{6}-[0-9]+$");
        if (!match.Success) return false;
        var saved = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(match.Groups[1].Value, 16)).UtcDateTime;
        return saved >= LeagueSeason.StartOf(now);
    }

    // The site's open team data answers in well under a second, against ~3 s
    // for a team page IBBA hasn't cached: its crest, and its newest league
    // that isn't a cup - this season's, once the season has begun.
    public Task<List<LeagueCup>> GetCupsAsync(string teamUrl) => OpenData().GetCupsAsync(teamUrl);

    // Each game's published lines (the open data's game - its address is its
    // code), and the player's own id from their page, read only when some
    // game has lines at all. Most leagues publish none, so a game the API's
    // list leaves out is looked up through its page only once the league is
    // known to publish: another game has lines, or - when the list has none
    // of the games - the first one's page shows lines. The first code is the
    // newest game that's had time for its line (the caller orders them).
    // The leagues IBBA publishes players' lines for - checked October 2026
    // across this season's games: every other league (youth below the top
    // tier, district leagues, cups) had none, and so did the men's top
    // league (its lines live on the Winner league's own site).
    private static readonly HashSet<string> LeaguesWithLines =
        ["לאומית", "ארצית צפון", "ארצית דרום", "נוער על צפון", "נוער על דרום", "ליגת על נשים", "לאומית נשים", "נערות א על"];

    public bool PublishesBoxScores(string? leagueName) => leagueName != null && LeaguesWithLines.Contains(leagueName.Trim());

    public async Task<Dictionary<string, LeagueBoxScore>> GetBoxScoresAsync(string playerUrl, IReadOnlyCollection<string> gameCodes)
    {
        var openData = OpenData();
        var codes = gameCodes.ToList();
        var listed = new System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, Dictionary<string, System.Text.Json.JsonElement>>?>();
        await Parallel.ForEachAsync(codes, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (code, _) =>
            listed[code] = await openData.GetGameLinesAsync(code, viaPage: false));

        var withLines = listed.Where(g => g.Value is { Count: > 0 }).ToDictionary(g => g.Key, g => g.Value!);
        var unlisted = codes.Where(c => listed[c] == null).ToList();
        if (withLines.Count == 0 && unlisted.Count == codes.Count && unlisted.Count > 0)
        {
            var newest = await openData.GetGameLinesAsync(unlisted[0]);
            unlisted.RemoveAt(0);
            if (newest is { Count: > 0 }) withLines[codes[0]] = newest;
        }
        if (withLines.Count == 0) return [];

        var found = new System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, Dictionary<string, System.Text.Json.JsonElement>>>(withLines);
        await Parallel.ForEachAsync(unlisted, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (code, _) =>
        {
            if (await openData.GetGameLinesAsync(code) is { Count: > 0 } lines) found[code] = lines;
        });

        var playerId = await new IbbaPlayerScraper(Http()).GetPlayerSiteIdAsync(playerUrl);
        if (playerId == null) return [];
        return found
            .Select(g => (Code: g.Key, Line: g.Value.GetValueOrDefault(playerId)))
            .Where(g => g.Line != null)
            .ToDictionary(g => g.Code, g => IbbaOpenData.ToBoxScore(g.Line!));
    }

    public async Task<LeagueTeamQuick?> QuickTeamAsync(string teamUrl)
    {
        var openData = OpenData();
        var info = await openData.FindTeamAsync(teamUrl);
        if (info == null) return null;
        var league = await openData.FindNewestLeagueAsync(info.LeagueIds);
        return new LeagueTeamQuick(info.CrestUrl, league?.Name, league?.PageUrl);
    }
}
