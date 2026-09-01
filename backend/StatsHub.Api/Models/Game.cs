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
        public bool? IsHomeGame { get; set; }

        // The opponent's IbbaTeam, resolved by team id at sync time (never by
        // name) - null for a manually-created game, or an IBBA game whose
        // opponent isn't in the same synced league yet.
        public int? OpponentIbbaTeamId { get; set; }

        // Set once the "starting soon" push has gone out for this game, so the
        // reminder background service never sends it twice.
        public DateTime? ReminderSentAt { get; set; }

        // Navigation properties
        public Team Team { get; set; } = null!;
        public IbbaTeam? OpponentIbbaTeam { get; set; }
        public ICollection<GameStats> GameStats { get; set; } = new List<GameStats>();
    }
}
