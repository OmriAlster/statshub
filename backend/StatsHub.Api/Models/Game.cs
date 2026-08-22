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

        // IBBA sync - all null for manually-created (including Friendly) games.
        // IbbaGameCode is the dedup key from IBBA's own per-game "Code" column,
        // so re-syncing never creates duplicate Games for the same fixture.
        public string? IbbaGameCode { get; set; }
        public int? IbbaTeamLinkId { get; set; }
        public bool? IsHomeGame { get; set; }

        // Navigation properties
        public Team Team { get; set; } = null!;
        public IbbaTeamLink? IbbaTeamLink { get; set; }
        public ICollection<GameStats> GameStats { get; set; } = new List<GameStats>();
    }
}
