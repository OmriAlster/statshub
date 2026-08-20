namespace StatsHub.Api.Models
{
    public class Game
    {
        public int Id { get; set; }
        public int TeamId { get; set; }
        public string GameType { get; set; } = "League"; // League or Cup
        public string OpponentName { get; set; } = string.Empty;
        public DateTime GameDate { get; set; }
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = "Upcoming"; // Upcoming, In Progress, Completed
        public int? TeamScore { get; set; }
        public int? OpponentScore { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Team Team { get; set; } = null!;
        public ICollection<GameStats> GameStats { get; set; } = new List<GameStats>();
    }
}
