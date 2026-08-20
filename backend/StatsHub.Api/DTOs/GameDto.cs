namespace StatsHub.Api.DTOs
{
    public class GameDto
    {
        public int Id { get; set; }
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string GameType { get; set; } = "League";
        public string OpponentName { get; set; } = string.Empty;
        public DateTime GameDate { get; set; }
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int? TeamScore { get; set; }
        public int? OpponentScore { get; set; }
        public string? Notes { get; set; }
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
    }
}
