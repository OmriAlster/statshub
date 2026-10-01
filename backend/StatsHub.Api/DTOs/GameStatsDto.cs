namespace StatsHub.Api.DTOs
{
    public class GameStatsDto
    {
        public Guid Id { get; set; }
        public Guid GameId { get; set; }
        public Guid PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        
        public int FieldGoalsMade { get; set; }
        public int FieldGoalsAttempted { get; set; }
        public double FieldGoalPercentage { get; set; }
        
        public int ThreePointersMade { get; set; }
        public int ThreePointersAttempted { get; set; }
        public double ThreePointPercentage { get; set; }
        
        public int FreeThrowsMade { get; set; }
        public int FreeThrowsAttempted { get; set; }
        public double FreeThrowPercentage { get; set; }
        
        public int OffensiveRebounds { get; set; }
        public int DefensiveRebounds { get; set; }
        public int TotalRebounds { get; set; }
        
        public int Assists { get; set; }
        public int Steals { get; set; }
        public int Blocks { get; set; }
        public int Turnovers { get; set; }
        public int Fouls { get; set; }
        public int MinutesPlayed { get; set; }
        public bool? OnCourt { get; set; }
        public int TotalPoints { get; set; }

        // Only populated where a caller specifically asks for it (e.g. a shared
        // game view) - not loaded by the normal authenticated game endpoints,
        // which fetch shots separately via /shots/gamestats/{id}.
        public List<ShotDto> Shots { get; set; } = new();
    }

    public class CreateGameStatsDto
    {
        public Guid GameId { get; set; }
        public Guid PlayerId { get; set; }
        
        public int FieldGoalsMade { get; set; }
        public int FieldGoalsAttempted { get; set; }
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
        public int MinutesPlayed { get; set; }
    }

    public class UpdateGameStatsDto
    {
        public int? FieldGoalsMade { get; set; }
        public int? FieldGoalsAttempted { get; set; }
        public int? ThreePointersMade { get; set; }
        public int? ThreePointersAttempted { get; set; }
        public int? FreeThrowsMade { get; set; }
        public int? FreeThrowsAttempted { get; set; }
        
        public int? OffensiveRebounds { get; set; }
        public int? DefensiveRebounds { get; set; }
        
        public int? Assists { get; set; }
        public int? Steals { get; set; }
        public int? Blocks { get; set; }
        public int? Turnovers { get; set; }
        public int? Fouls { get; set; }
        public int? MinutesPlayed { get; set; }
        public bool? OnCourt { get; set; }
    }
}
