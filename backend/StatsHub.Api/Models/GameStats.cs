namespace StatsHub.Api.Models
{
    public class GameStats
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid GameId { get; set; }
        public Guid PlayerId { get; set; }
        
        // Where the numbers came from: null - the parent (typed in or tracked
        // live); "box" - the league's published box score; "scoresheet" - read
        // off the game's scoresheet. Only a row the league filled is ever
        // updated by a sync - once a parent edits it, it's theirs.
        public string? Source { get; set; }
        public const string BoxScoreSource = "box";
        public const string ScoresheetSource = "scoresheet";

        // Scoring. A null stat wasn't recorded (a scoresheet has the made
        // shots and fouls, nothing else) - shown as "-" and left out of that
        // stat's averages and of the shooting percentages.
        public int FieldGoalsMade { get; set; }
        public int? FieldGoalsAttempted { get; set; }
        public int ThreePointersMade { get; set; }
        public int? ThreePointersAttempted { get; set; }
        public int FreeThrowsMade { get; set; }
        public int? FreeThrowsAttempted { get; set; }
        
        // Rebounds
        public int? OffensiveRebounds { get; set; }
        public int? DefensiveRebounds { get; set; }
        
        // Other Stats
        public int? Assists { get; set; }
        public int? Steals { get; set; }
        public int? Blocks { get; set; }
        public int? Turnovers { get; set; }
        public int Fouls { get; set; }
        public int? MinutesPlayed { get; set; }
        // During a live game: is the player on the court right now, as the
        // tracker last set it with Sub in/out. Null = not tracked.
        public bool? OnCourt { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Game Game { get; set; } = null!;
        public Player Player { get; set; } = null!;
        public ICollection<Shot> Shots { get; set; } = new List<Shot>();

        // Calculated properties
        public double? FieldGoalPercentage => Percentage(FieldGoalsMade, FieldGoalsAttempted);

        public double? ThreePointPercentage => Percentage(ThreePointersMade, ThreePointersAttempted);

        public double? FreeThrowPercentage => Percentage(FreeThrowsMade, FreeThrowsAttempted);

        // Null when the attempts weren't recorded; 0 for no attempts.
        public static double? Percentage(int made, int? attempted) =>
            attempted is null ? null : attempted > 0 ? Math.Round((double)made / attempted.Value * 100, 2) : 0;

        // Null when neither kind was recorded.
        public int? TotalRebounds => OffensiveRebounds is null && DefensiveRebounds is null ? null : (OffensiveRebounds ?? 0) + (DefensiveRebounds ?? 0);

        public int TotalPoints => (FieldGoalsMade * 2) + (ThreePointersMade * 3) + FreeThrowsMade;
    }
}
