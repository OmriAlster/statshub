namespace StatsHub.Api.Leagues;

/// <summary>
/// One external league the app can link players to and sync from - the only
/// part that knows the league's own site. Everything else (saving teams,
/// games and standings, cancelled games, results soon after games, the
/// nightly sync, notifications, live updates) is shared, in IbbaService.
///
/// To add a league: implement this (reading the league's pages or API into
/// the shapes in LeagueModels.cs), register it in Program.cs with
/// AddSingleton&lt;ILeagueProvider, YourProvider&gt;(), and give it a logo under
/// frontend/public/icons. Players are linked by pasting a player's address -
/// the provider whose IsPlayerUrl accepts it handles them from then on.
/// </summary>
public interface ILeagueProvider
{
    /// <summary>Stored on every link, team and game it syncs ("ibba"). Never change it once used.</summary>
    string Key { get; }

    /// <summary>The name parents see ("IBBA").</summary>
    string Name { get; }

    /// <summary>The league's mark, as a path the website serves ("/icons/ibba-logo.png").</summary>
    string LogoUrl { get; }

    /// <summary>An example player address, shown where a parent pastes one.</summary>
    string PlayerUrlExample { get; }

    /// <summary>Whether this address is a player's page on this league's site.</summary>
    bool IsPlayerUrl(string url);

    /// <summary>The player's own page: name, birth date, photo and current teams.</summary>
    Task<LeaguePlayer> GetPlayerAsync(string playerUrl);

    /// <summary>
    /// Stage 1 of a sync (the quick part): each of the player's teams with its
    /// final name and crest. prefetchedPlayer is GetPlayerAsync's result when
    /// the caller already has it.
    /// </summary>
    Task<(LeaguePlayer Player, List<LeagueTeamPage> Teams)> LoadTeamsAsync(string playerUrl, LeaguePlayer? prefetchedPlayer = null);

    /// <summary>Stage 2 (the slow part): a team's games, and its league and league table.</summary>
    Task<LeagueTeamReport> LoadGamesAndStandingsAsync(LeagueTeamPage page);

    /// <summary>Just a team's games, by its address - for checking results soon after a game.</summary>
    Task<List<LeagueGame>> LoadTeamGamesAsync(string teamUrl);

    /// <summary>A team known only by the id a game uses for it (e.g. a cup opponent from another league).</summary>
    Task<(string CanonicalUrl, string Name, string? LogoUrl)?> ResolveTeamByIdAsync(string teamId);

    /// <summary>A team's crest, or null when its page has none.</summary>
    Task<string?> FindTeamLogoUrlAsync(string teamUrl, string teamName);

    /// <summary>The league's id for a team, read from its address (same id as LeagueTeam.TeamSlugId).</summary>
    string TeamIdFromUrl(string teamUrl);

    /// <summary>
    /// Players on the league's site whose name matches - name and page address
    /// only, so the list can show at once (each one's page, GetPlayerAsync,
    /// fills in their teams and birth date after). A league without a search
    /// returns none.
    /// </summary>
    Task<List<LeaguePlayerHit>> SearchPlayersAsync(string name) => Task.FromResult(new List<LeaguePlayerHit>());

    /// <summary>
    /// A team's crest without loading its page, when the league offers a quick
    /// way (for a found player's card); null when it doesn't or has none.
    /// </summary>
    Task<string?> QuickTeamLogoUrlAsync(string teamUrl) => Task.FromResult<string?>(null);
}
