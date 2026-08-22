namespace StatsHub.Api.IbbaScraping;

/// <summary>
/// One team the player is found under (main "קבוצה נוכחית" or a רשאי/ת additional team).
/// No distinction is kept between the two - both are just "a team this player plays for."
/// </summary>
public class IbbaPlayerTeamInfo
{
    public string TeamName { get; set; } = "";
    public string TeamUrl { get; set; } = "";
    /// <summary>Numeric team id parsed out of the team href, e.g. 10550 from /team/10550-.../</summary>
    public string TeamSlugId { get; set; } = "";
    public string TeamLogoUrl { get; set; } = "";

    public string LeagueName { get; set; } = "";
    public string LeagueUrl { get; set; } = "";
}

/// <summary>
/// Everything extracted from the player's profile page.
/// </summary>
public class IbbaPlayerInfo
{
    public string PlayerName { get; set; } = "";
    public string PlayerUrl { get; set; } = "";
    public List<IbbaPlayerTeamInfo> Teams { get; set; } = new();
}

/// <summary>
/// A single row from the team's "games" Excel export. Real column layout (verified against
/// a live export - the site's own column order, not documented anywhere):
/// ליגה | Code | Week Day | תאריך | מחזור | Time | Home Team | Home Team Code |
/// Away Team | Away Team Code | Venue | Home Score | Away Score
/// </summary>
public class IbbaGameRow
{
    public string League { get; set; } = "";
    public string Code { get; set; } = "";           // stable per-game id, used for dedup on re-sync
    public string WeekDay { get; set; } = "";
    public string Date { get; set; } = "";
    public string Round { get; set; } = "";
    public string Time { get; set; } = "";
    public string HomeTeam { get; set; } = "";
    public string HomeTeamCode { get; set; } = "";
    public string AwayTeam { get; set; } = "";
    public string AwayTeamCode { get; set; } = "";
    public string Venue { get; set; } = "";
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    public bool IsCup => League.Contains("גביע");
}

/// <summary>
/// A single row from the league standings table.
/// </summary>
public class IbbaStandingRow
{
    public int Position { get; set; }            // מיקום
    public string TeamName { get; set; } = "";    // קבוצה
    public string TeamUrl { get; set; } = "";
    public int GamesPlayed { get; set; }          // מש'
    public int Wins { get; set; }                 // ניצ'
    public int Losses { get; set; }                // הפ'
    public int Technical { get; set; }            // טכני
    public int PointsFor { get; set; }            // קלעה
    public int PointsAgainst { get; set; }        // ספגה
    public int Diff { get; set; }                 // הפרש
    public int LeaguePoints { get; set; }         // נק'

    public bool IsTeam(string teamNameOrUrl) =>
        TeamName.Contains(teamNameOrUrl, StringComparison.OrdinalIgnoreCase) ||
        TeamUrl.Contains(teamNameOrUrl, StringComparison.OrdinalIgnoreCase);
}
