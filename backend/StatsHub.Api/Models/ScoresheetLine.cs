namespace StatsHub.Api.Models
{
    // A player's line as the game's scoresheet has it - kept apart from
    // GameStats, which the parent's own numbers win, so the league's version
    // can still be shown next to theirs. One per player per game.
    public class ScoresheetLine
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid GameId { get; set; }
        public Guid PlayerId { get; set; }
        public int Number { get; set; }
        // The player code printed on the sheet - stable across games, unlike
        // the number.
        public string Code { get; set; } = string.Empty;
        public bool Played { get; set; }
        public bool Starter { get; set; }
        public int Points { get; set; }
        public int FreeThrows { get; set; }
        public int TwoPointers { get; set; }
        public int ThreePointers { get; set; }
        public int Fouls { get; set; }
        // The sheet's own cross-checks (players' points vs the running score,
        // the quarter and final scores) - a sheet that fails them is still
        // used, with the problems shown.
        public bool SheetValid { get; set; }
        public string? SheetErrors { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Game Game { get; set; } = null!;
        public Player Player { get; set; } = null!;
    }
}
