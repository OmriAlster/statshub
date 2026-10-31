namespace StatsHub.Api.Leagues;

// What every league provider (ILeagueProvider) reads off its site, in one
// shape - the sync (IbbaService) works only with these, never with a
// league's own pages.

/// <summary>A team the player currently plays for.</summary>
public class LeagueTeam
{
    public string TeamName { get; set; } = "";
    public string TeamUrl { get; set; } = "";
    /// <summary>The league's own id for the team - the same id its games use for their two sides.</summary>
    public string TeamSlugId { get; set; } = "";
    public string TeamLogoUrl { get; set; } = "";

    public string LeagueName { get; set; } = "";
    public string LeagueUrl { get; set; } = "";

    /// <summary>A team the player is also allowed to play for, besides their main team.</summary>
    public bool IsAdditional { get; set; }
}

/// <summary>Everything read off the player's own page.</summary>
public class LeaguePlayer
{
    public string PlayerName { get; set; } = "";
    public string PlayerUrl { get; set; } = "";
    public List<LeagueTeam> Teams { get; set; } = new();
    public DateTime? DateOfBirth { get; set; }
    public string PhotoUrl { get; set; } = "";
}

/// <summary>The basketball season - August to July. Only the current one matters.</summary>
public static class LeagueSeason
{
    public static DateTime StartOf(DateTime now) => new(now.Month >= 8 ? now.Year : now.Year - 1, 8, 1, 0, 0, 0, DateTimeKind.Utc);
}

/// <summary>What a found player's card shows of a team the app hasn't synced this season.</summary>
public record LeagueTeamQuick(string? CrestUrl, string? LeagueName, string? LeagueUrl);

/// <summary>A player's line in one game, as the league published it.</summary>
public class LeagueBoxScore
{
    public int TwoPointersMade { get; set; }
    public int TwoPointersAttempted { get; set; }
    public int ThreePointersMade { get; set; }
    public int ThreePointersAttempted { get; set; }
    public int FreeThrowsMade { get; set; }
    public int FreeThrowsAttempted { get; set; }
    public int OffensiveRebounds { get; set; }
    public int DefensiveRebounds { get; set; }
    public int Assists { get; set; }
    public int Steals { get; set; }
    public int Blocks { get; set; }
    public int Turnovers { get; set; }
    public int Fouls { get; set; }
    public int Minutes { get; set; }
}

/// <summary>A cup this season, as its bracket: rounds of games, first round first.</summary>
public class LeagueCup
{
    public string Name { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public List<LeagueCupRound> Rounds { get; set; } = new();
}

public class LeagueCupRound
{
    public string Name { get; set; } = "";
    public List<LeagueCupGame> Games { get; set; } = new();
}

/// <summary>One game of a bracket - its sides empty until the earlier round decides them.</summary>
public class LeagueCupGame
{
    public LeagueCupSide? Home { get; set; }
    public LeagueCupSide? Away { get; set; }
    /// <summary>As the league's site shows it (Israel time), when the game is scheduled.</summary>
    public DateTime? LocalDate { get; set; }
}

public class LeagueCupSide
{
    public string Name { get; set; } = "";
    public string TeamUrl { get; set; } = "";
    public string? LogoUrl { get; set; }
    public int? Score { get; set; }
    public bool Won { get; set; }
}

/// <summary>A player a name search found: just who and where - their page has the rest.</summary>
public record LeaguePlayerHit(string PlayerName, string PlayerUrl);

/// <summary>One game of a team's schedule.</summary>
public class LeagueGame
{
    public string League { get; set; } = "";
    public string Code { get; set; } = "";           // the league's own id for the game - stable across syncs
    public string WeekDay { get; set; } = "";
    public string Date { get; set; } = "";           // d-M-yyyy, local (Israel) time
    public string Round { get; set; } = "";
    public string Time { get; set; } = "";           // H:mm, or empty
    public string HomeTeam { get; set; } = "";
    public string HomeTeamCode { get; set; } = "";   // the league's team ids (LeagueTeam.TeamSlugId)
    public string AwayTeam { get; set; } = "";
    public string AwayTeamCode { get; set; } = "";
    public string Venue { get; set; } = "";
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public bool IsCup { get; set; }
}

/// <summary>One row of a league table.</summary>
public class LeagueStanding
{
    public int Position { get; set; }
    public string TeamName { get; set; } = "";
    public string TeamUrl { get; set; } = "";
    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Technical { get; set; }
    public int PointsFor { get; set; }
    public int PointsAgainst { get; set; }
    public int Diff { get; set; }
    public int LeaguePoints { get; set; }

    // Exact match only - a substring match (e.g. "מכבי בקה" is contained in
    // "מכבי בקה גת") would silently pick the wrong row whenever one team's
    // name happens to be a prefix of another's.
    public bool IsTeam(string? teamUrl, string teamName) =>
        (!string.IsNullOrEmpty(teamUrl) && !string.IsNullOrEmpty(TeamUrl) && string.Equals(TeamUrl, teamUrl, StringComparison.OrdinalIgnoreCase)) ||
        string.Equals(TeamName, teamName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A team's page as loaded in the quick first stage of a sync, kept for the
/// slower second stage (games and standings). State is the provider's own -
/// for IBBA, the page itself and its spreadsheet link.
/// </summary>
public class LeagueTeamPage
{
    public LeagueTeam Team { get; set; } = new();
    public object? State { get; set; }
}

/// <summary>A team's games and its league table.</summary>
public class LeagueTeamReport
{
    public LeagueTeam Team { get; set; } = new();
    public List<LeagueGame> Games { get; set; } = new();
    public List<LeagueStanding> Standings { get; set; } = new();
    public LeagueStanding? TeamStanding => Standings.FirstOrDefault(s => s.IsTeam(Team.TeamUrl, Team.TeamName));
}

/// <summary>
/// The league's page or file no longer looks the way its provider expects (a
/// renamed column, a missing table header, a date in another format). Thrown
/// instead of returning half-read data: a sync that hits it records the error
/// on the link and saves nothing, rather than storing zeros, wrong dates or a
/// team read off the wrong link.
/// </summary>
public class LeagueLayoutException : Exception
{
    public const string UserMessage = "The league changed how its site looks, so this couldn't be read. Nothing was changed - it will work again once we update.";

    public string Details { get; }

    public LeagueLayoutException(string details) : base($"{UserMessage} ({details})") => Details = details;
}
