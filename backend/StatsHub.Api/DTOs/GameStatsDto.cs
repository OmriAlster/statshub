namespace StatsHub.Api.DTOs
{
    public class GameStatsDto
    {
        public Guid Id { get; set; }
        public Guid GameId { get; set; }
        public Guid PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        // Null - the parent's numbers; "box" / "scoresheet" - filled from the league.
        public string? Source { get; set; }
        
        public int FieldGoalsMade { get; set; }
        public int? FieldGoalsAttempted { get; set; }
        public double? FieldGoalPercentage { get; set; }
        
        public int ThreePointersMade { get; set; }
        public int? ThreePointersAttempted { get; set; }
        public double? ThreePointPercentage { get; set; }
        
        public int FreeThrowsMade { get; set; }
        public int? FreeThrowsAttempted { get; set; }
        public double? FreeThrowPercentage { get; set; }
        
        public int? OffensiveRebounds { get; set; }
        public int? DefensiveRebounds { get; set; }
        public int? TotalRebounds { get; set; }
        
        public int? Assists { get; set; }
        public int? Steals { get; set; }
        public int? Blocks { get; set; }
        public int? Turnovers { get; set; }
        public int Fouls { get; set; }
        public int? MinutesPlayed { get; set; }
        public bool? OnCourt { get; set; }
        public int TotalPoints { get; set; }

        // The player's line on the game's scoresheet - shown next to the
        // parent's own numbers, flagged where they differ.
        public ScoresheetLineDto? Scoresheet { get; set; }

        // Only populated where a caller specifically asks for it (e.g. a shared
        // game view) - not loaded by the normal authenticated game endpoints,
        // which fetch shots separately via /shots/gamestats/{id}.
        public List<ShotDto> Shots { get; set; } = new();
    }

    public class ScoresheetLineDto
    {
        public Guid PlayerId { get; set; }
        public int Number { get; set; }
        // Entered the game (the X in the sheet's "entered" column) - on the
        // sheet without it, the player didn't play.
        public bool Played { get; set; }
        public bool Starter { get; set; }
        public int Points { get; set; }
        public int FreeThrows { get; set; }
        public int TwoPointers { get; set; }
        public int ThreePointers { get; set; }
        public int Fouls { get; set; }
        // The sheet failed its own cross-checks - its numbers may be off.
        public bool SheetValid { get; set; }
        public string? SheetErrors { get; set; }
    }

    // The game's stats rebuilt from IBBA's scoresheet, in place of what was there.
    public class StatsFromScoresheetDto
    {
        public Guid GameId { get; set; }
        public Guid PlayerId { get; set; }
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
