namespace StatsHub.Api.Scoresheets;

/// <summary>
/// Everything read from one digital scoresheet PDF ("טופס משחק"). Team "Home" is the
/// sheet's team A (מארחת), "Away" is team B (אורחת).
/// </summary>
public sealed record ParsedScoresheet(
    string? Competition,
    string? Date,
    string? Time,
    ScoresheetTeam Home,
    ScoresheetTeam Away,
    IReadOnlyList<QuarterScore> Quarters,
    int? HomeFinal,
    int? AwayFinal,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// False when any cross-check failed (player points vs. the running score, vs. the
    /// printed quarter and final scores, unknown scorers...). An invalid sheet must not be
    /// written to the database - route it to review instead.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}

public sealed record ScoresheetTeam(
    string Name,
    int Points,
    IReadOnlyList<ScoresheetPlayer> Players,
    IReadOnlyList<ScoringEvent> Events);

public sealed record ScoresheetPlayer(
    int Number,
    // The player code printed in brackets, e.g. "491". Stable per player, so it is the
    // key to remember after the first name match.
    string Code,
    string LastName,
    // First-name initial only - that is all the sheet carries.
    string FirstInitial,
    bool Captain,
    // Marked in the "entered" column.
    bool Played,
    // Circled X: in the starting five.
    bool Starter,
    int Fouls,
    int Points,
    int FreeThrows,
    int TwoPointers,
    int ThreePointers);

/// <summary>One made basket, in running-score order.</summary>
public sealed record ScoringEvent(
    // The team's running score after this basket.
    int Score,
    // Jersey number of the scorer (null only when the sheet is broken - also reported as an error).
    int? Player,
    int Points,
    // "ft", "fg2" or "fg3".
    string Kind,
    // 1-4, 5+ for overtime.
    int Period);

public sealed record QuarterScore(int? Home, int? Away);
