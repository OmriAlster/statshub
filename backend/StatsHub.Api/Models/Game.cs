namespace StatsHub.Api.Models
{
    public class Game
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();

        public string GameType { get; set; } = "League"; // League, Cup or Friendly

        // Friendly games are tracked (schedule, live scoring, box score) but
        // never count toward season stats - averages, totals, shot charts.
        public const string FriendlyGameType = "Friendly";
        public DateTime GameDate { get; set; }
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = "Upcoming"; // Upcoming, In Progress, Completed
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Set once the "starting soon" push has gone out for this game, so the
        // reminder background service never sends it twice.
        public DateTime? ReminderSentAt { get; set; }

        // Whoever transitions this game to "In Progress" claims it - while
        // live, only they can keep recording (Game and GameStats writes both
        // check this); anyone else with access sees a read-only view instead
        // of two people racing to score the same game. Cleared once the game
        // leaves "In Progress" (completed, or reverted), so normal editing
        // rules apply again afterward.
        public Guid? LiveTrackedByUserId { get; set; }

        // The dedup key from IBBA's own per-game "Code" column - null for a
        // manually-created game. Also the discriminator for HomeTeamId/AwayTeamId
        // below: a single column can't carry a real foreign key to two different
        // tables, so which table it means is read off this field instead of a
        // formal EF relationship.
        public string? IbbaGameCode { get; set; }

        // The two sides of the fixture. For an IBBA-synced game, both are real
        // IbbaTeam ids - objective facts (home/away exactly as IBBA's own
        // export labels them), resolved by id at sync time, never by name.
        // For a manually-created game, at most one is set - your own app
        // Team id, on whichever side you're playing - since a manual
        // opponent isn't a tracked entity at all, just OpponentName below.
        public Guid? HomeTeamId { get; set; }
        public Guid? AwayTeamId { get; set; }

        // ---- Manually-created games only (IbbaGameCode == null) ----
        public string OpponentName { get; set; } = string.Empty;
        public int? TeamScore { get; set; }
        public int? OpponentScore { get; set; }

        // ---- IBBA-synced games only (IbbaGameCode != null) ----
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }

        // Navigation properties
        public ICollection<GameStats> GameStats { get; set; } = new List<GameStats>();
    }
}
