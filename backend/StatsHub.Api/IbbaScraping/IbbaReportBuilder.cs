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
    public IbbaStandingRow? TeamStanding => Standings.FirstOrDefault(s => s.IsTeam(Team.TeamName));
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

    public async Task<(IbbaPlayerInfo Player, List<IbbaTeamReport> Teams)> BuildAsync(string playerUrl)
    {
        var player = await _playerScraper.GetPlayerInfoAsync(playerUrl);

        if (player.Teams.Count == 0)
            throw new InvalidOperationException("Could not find any current team for this player on the page.");

        var teamReports = new List<IbbaTeamReport>();
        foreach (var team in player.Teams)
        {
            var report = new IbbaTeamReport { Team = team };

            // Use the team's own page as the source of truth for its name - the player
            // page's link text isn't always just the plain name (רשאי links append
            // " - LeagueName"), which would break exact-match lookups below.
            team.TeamName = await _teamScraper.GetTeamNameAsync(team.TeamUrl) ?? team.TeamName;

            team.TeamLogoUrl = await _teamScraper.FindTeamLogoUrlAsync(team.TeamUrl, team.TeamName) ?? "";

            var excelUrl = await _teamScraper.FindExcelExportUrlAsync(team.TeamUrl);
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
                team.LeagueUrl = await _teamScraper.FindLeagueUrlAsync(team.TeamUrl, leagueName) ?? "";
                if (!string.IsNullOrEmpty(team.LeagueUrl))
                    report.Standings = await _leagueScraper.GetStandingsAsync(team.LeagueUrl);
            }

            teamReports.Add(report);
        }

        return (player, teamReports);
    }
}
