namespace StatsHub.Api.DTOs
{
    // Aggregated stats for one player on one team - computed on demand from
    // GameStats rather than stored, since a player can be on multiple teams
    // and stats need to stay split per team.
    public class PlayerTeamStatsDto
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public int JerseyNumber { get; set; }
        public string Position { get; set; } = string.Empty;
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;

        public int GamesPlayed { get; set; }
        public int TotalMinutes { get; set; }
        public int TotalPoints { get; set; }
        public double PointsPerGame { get; set; }
        public int TotalRebounds { get; set; }
        public double ReboundsPerGame { get; set; }
        public int TotalAssists { get; set; }
        public double AssistsPerGame { get; set; }
        public int TotalSteals { get; set; }
        public double StealsPerGame { get; set; }
        public int TotalBlocks { get; set; }
        public double BlocksPerGame { get; set; }
        public int TotalTurnovers { get; set; }
        public double TurnoversPerGame { get; set; }

        public double FieldGoalPercentage { get; set; }
        public double ThreePointPercentage { get; set; }
        public double FreeThrowPercentage { get; set; }
    }
}
