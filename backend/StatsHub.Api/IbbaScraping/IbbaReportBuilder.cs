using StatsHub.Api.Services;
namespace StatsHub.Api.IbbaScraping;

/// <summary>
/// One team's worth of scraped results: games (from that team's Excel export) and its
/// league standings (with this team's position highlighted).
/// </summary>
public class IbbaTeamReport
{
    public IbbaPlayerTeamInfo Team { get; set; } = new();
    public List<IbbaGameRow> Games { get; set; } = new();
    public List<IbbaStandingRow> Standings { get; set; } = new();
    public IbbaStandingRow? TeamStanding => Standings.FirstOrDefault(s => s.IsTeam(Team.TeamUrl, Team.TeamName));
}

/// <summary>
/// A team's own page, already loaded - what the games/standings stage needs
/// (export link, and the page itself to find the league link on).
/// </summary>
public class IbbaTeamPage
{
    public IbbaPlayerTeamInfo Team { get; set; } = new();
    public HtmlAgilityPack.HtmlDocument Doc { get; set; } = new();
    public string? ExcelUrl { get; set; }
}

/// <summary>
/// Ties IbbaPlayerScraper + IbbaTeamScraper + IbbaLeagueScraper together: player -> every
/// current team (main + any רשאי), and per team - logo, league, games (from the Excel
/// export), and league standings.
///
/// This is a standalone, isolated service - it does not touch the database or know
/// anything about StatsHub's own Player/Team/Game models. Sync/persistence is a separate
/// layer built on top of this.
/// </summary>
public class IbbaReportBuilder
{
    private readonly IbbaPlayerScraper _playerScraper;
    private readonly IbbaTeamScraper _teamScraper;
    private readonly IbbaLeagueScraper _leagueScraper;

    public IbbaReportBuilder(HttpClient http)
    {
        _playerScraper = new IbbaPlayerScraper(http);
        _teamScraper = new IbbaTeamScraper(http);
        _leagueScraper = new IbbaLeagueScraper(http);
    }

    // prefetchedPlayer lets a caller that already fetched the player's own
    // page (e.g. previewing it to validate before creating a StatsHub player
    // from it) hand that result straight in, instead of this doing the exact
    // same fetch again a moment later.
    //
    // Two stages, so a link can answer as soon as the teams are known:
    // LoadTeamsAsync (player page + each team's own page - quick, usually
    // cached by IBBA) and LoadGamesAndStandingsAsync (the games spreadsheet,
    // always fetched fresh and IBBA's slowest page, then the league table).
    public async Task<(IbbaPlayerInfo Player, List<IbbaTeamReport> Teams)> BuildAsync(string playerUrl, IbbaPlayerInfo? prefetchedPlayer = null)
    {
        var (player, pages) = await LoadTeamsAsync(playerUrl, prefetchedPlayer);
        var teamReports = (await Task.WhenAll(pages.Select(LoadGamesAndStandingsAsync))).ToList();
        return (player, teamReports);
    }

    // The player and every team they're on (name, crest, export link). Every
    // team at once - order matches player.Teams.
    public async Task<(IbbaPlayerInfo Player, List<IbbaTeamPage> Teams)> LoadTeamsAsync(string playerUrl, IbbaPlayerInfo? prefetchedPlayer = null)
    {
        var player = prefetchedPlayer ?? await _playerScraper.GetPlayerInfoAsync(playerUrl);

        if (player.Teams.Count == 0)
            throw new InvalidOperationException("Could not find any current team for this player on the page.");

        var pages = (await Task.WhenAll(player.Teams.Select(LoadTeamPageAsync))).ToList();
        return (player, pages);
    }

    private async Task<IbbaTeamPage> LoadTeamPageAsync(IbbaPlayerTeamInfo team)
    {
        // One fetch of the team's own page covers name, crest, and the Excel
        // export link (and later the league link).
        var doc = await _teamScraper.LoadTeamPageAsync(team.TeamUrl);

        using (RequestTimings.Time("find-name-crest-export-link"))
        {
            // The team's own page is the source of truth for its name - the player
            // page's link text isn't always just the plain name (רשאי links append
            // " - LeagueName"), which would break exact-match lookups later.
            team.TeamName = _teamScraper.GetTeamName(doc) ?? team.TeamName;
            team.TeamLogoUrl = _teamScraper.FindTeamLogo(doc, team.TeamUrl, team.TeamName) ?? "";
            return new IbbaTeamPage { Team = team, Doc = doc, ExcelUrl = _teamScraper.FindExcelExportUrl(doc, team.TeamUrl) };
        }
    }

    // A team's games (its Excel export) and, from those, its league and the
    // league's standings.
    public async Task<IbbaTeamReport> LoadGamesAndStandingsAsync(IbbaTeamPage page)
    {
        using var whole = RequestTimings.Time("team-games-and-standings");
        var team = page.Team;
        var report = new IbbaTeamReport { Team = team };

        if (page.ExcelUrl != null)
            report.Games = await _teamScraper.DownloadAndParseGamesAsync(page.ExcelUrl);

        // Resolve the league from the regular-season (non-cup) games just parsed -
        // the team page's league directory lets us find ANY team's league this way,
        // not just the main team's (unlike the player page's "ליגה" label, which only
        // labels the main team).
        var leagueName = report.Games.FirstOrDefault(g => !g.IsCup)?.League;
        if (!string.IsNullOrEmpty(leagueName))
        {
            team.LeagueName = leagueName;
            team.LeagueUrl = _teamScraper.FindLeagueUrl(page.Doc, team.TeamUrl, leagueName) ?? "";
            if (!string.IsNullOrEmpty(team.LeagueUrl))
                report.Standings = await _leagueScraper.GetStandingsAsync(team.LeagueUrl);
        }

        return report;
    }
}
