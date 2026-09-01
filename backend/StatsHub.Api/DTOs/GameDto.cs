namespace StatsHub.Api.DTOs
{
    public class GameDto
    {
        public int Id { get; set; }
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string GameType { get; set; } = "League";
        public string OpponentName { get; set; } = string.Empty;
        // Only ever populated for an IBBA-synced game whose opponent is also a
        // team in the same synced league's standings - there's no logo source
        // for a manually-created game's opponent.
        public string? OpponentLogoUrl { get; set; }
        public DateTime GameDate { get; set; }
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int? TeamScore { get; set; }
        public int? OpponentScore { get; set; }
        public string? Notes { get; set; }
        public bool? IsHomeGame { get; set; }
        public bool IsFromIbba { get; set; }
        public List<GameStatsDto> PlayerStats { get; set; } = new List<GameStatsDto>();
    }

    public class CreateGameDto
    {
        public int TeamId { get; set; }
        public string GameType { get; set; } = "League";
        public string OpponentName { get; set; } = string.Empty;
        public DateTime GameDate { get; set; }
        public string Location { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public class UpdateGameDto
    {
        public string? OpponentName { get; set; }
        public DateTime? GameDate { get; set; }
        public string? Location { get; set; }
        public string? Status { get; set; }
        public string? GameType { get; set; }
        public int? TeamScore { get; set; }
        public int? OpponentScore { get; set; }
        public string? Notes { get; set; }
        public bool? IsHomeGame { get; set; }
    }
}
