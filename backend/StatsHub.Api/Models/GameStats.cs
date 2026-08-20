namespace StatsHub.Api.Models
{
    public class GameStats
    {
        public int Id { get; set; }
        public int GameId { get; set; }
        public int PlayerId { get; set; }
        
        // Scoring
        public int FieldGoalsMade { get; set; }
        public int FieldGoalsAttempted { get; set; }
        public int ThreePointersMade { get; set; }
        public int ThreePointersAttempted { get; set; }
        public int FreeThrowsMade { get; set; }
        public int FreeThrowsAttempted { get; set; }
        
        // Rebounds
        public int OffensiveRebounds { get; set; }
        public int DefensiveRebounds { get; set; }
        
        // Other Stats
        public int Assists { get; set; }
        public int Steals { get; set; }
        public int Blocks { get; set; }
        public int Turnovers { get; set; }
        public int Fouls { get; set; }
        public int MinutesPlayed { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Game Game { get; set; } = null!;
        public Player Player { get; set; } = null!;
        public ICollection<Shot> Shots { get; set; } = new List<Shot>();

        // Calculated properties
        public double FieldGoalPercentage =>
            FieldGoalsAttempted > 0 ? Math.Round((double)FieldGoalsMade / FieldGoalsAttempted * 100, 2) : 0;

        public double ThreePointPercentage =>
            ThreePointersAttempted > 0 ? Math.Round((double)ThreePointersMade / ThreePointersAttempted * 100, 2) : 0;

        public double FreeThrowPercentage =>
            FreeThrowsAttempted > 0 ? Math.Round((double)FreeThrowsMade / FreeThrowsAttempted * 100, 2) : 0;

        public int TotalRebounds => OffensiveRebounds + DefensiveRebounds;

        public int TotalPoints => (FieldGoalsMade * 2) + (ThreePointersMade * 3) + FreeThrowsMade;
    }
}
