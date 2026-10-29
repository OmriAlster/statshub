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

        if (state.ExcelUrl != null)
            report.Games = await teamScraper.DownloadAndParseGamesAsync(state.ExcelUrl);

        // Resolve the league from the regular-season (non-cup) games just parsed -
        // the team page's league directory lets us find ANY team's league this way,
        // not just the main team's (unlike the player page's "ליגה" label, which only
        // labels the main team).
        var leagueName = report.Games.FirstOrDefault(g => !g.IsCup)?.League;
        if (string.IsNullOrEmpty(leagueName)) return report;
        team.LeagueName = leagueName;

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
        var seasonStart = new DateTime(now.Month >= 8 ? now.Year : now.Year - 1, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        return saved >= seasonStart;
    }

    // The site's open team data (/team/<slug>/ -> its crest image) answers in
    // well under a second, against ~3 s for a team page IBBA hasn't cached.
    public async Task<string?> QuickTeamLogoUrlAsync(string teamUrl) => (await OpenData().FindTeamAsync(teamUrl))?.CrestUrl;
}
