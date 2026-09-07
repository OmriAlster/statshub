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
    // same fetch again a moment later - a real, previously-unnecessary full
    // page load on the "create player from IBBA" path specifically.
    public async Task<(IbbaPlayerInfo Player, List<IbbaTeamReport> Teams)> BuildAsync(string playerUrl, IbbaPlayerInfo? prefetchedPlayer = null)
    {
        var player = prefetchedPlayer ?? await _playerScraper.GetPlayerInfoAsync(playerUrl);

        if (player.Teams.Count == 0)
            throw new InvalidOperationException("Could not find any current team for this player on the page.");

        var teamReports = new List<IbbaTeamReport>();
        foreach (var team in player.Teams)
        {
            var report = new IbbaTeamReport { Team = team };

            // One fetch of the team's own page covers name, crest, and the Excel
            // export link - these used to be three (four, counting the league
            // lookup below) separate methods that each fetched and re-parsed
            // this exact same URL from scratch, which was most of the real
            // cost behind "creating a player from IBBA" feeling slow.
            var doc = await _teamScraper.LoadTeamPageAsync(team.TeamUrl);

            // Use the team's own page as the source of truth for its name - the player
            // page's link text isn't always just the plain name (רשאי links append
            // " - LeagueName"), which would break exact-match lookups below.
            team.TeamName = _teamScraper.GetTeamName(doc) ?? team.TeamName;

            team.TeamLogoUrl = _teamScraper.FindTeamLogo(doc, team.TeamUrl, team.TeamName) ?? "";

            var excelUrl = _teamScraper.FindExcelExportUrl(doc, team.TeamUrl);
            if (excelUrl != null)
                report.Games = await _teamScraper.DownloadAndParseGamesAsync(excelUrl);

            // Resolve the league from the regular-season (non-cup) games we just parsed -
            // the team page's league directory lets us find ANY team's league this way,
            // not just the main team's (unlike the player page's "ליגה" label, which only
            // labels the main team).
            var leagueName = report.Games.FirstOrDefault(g => !g.IsCup)?.League;
            if (!string.IsNullOrEmpty(leagueName))
            {
                team.LeagueName = leagueName;
                team.LeagueUrl = _teamScraper.FindLeagueUrl(doc, team.TeamUrl, leagueName) ?? "";
                if (!string.IsNullOrEmpty(team.LeagueUrl))
                    report.Standings = await _leagueScraper.GetStandingsAsync(team.LeagueUrl);
            }

            teamReports.Add(report);
        }

        return (player, teamReports);
    }
}
