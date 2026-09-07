namespace StatsHub.Api.Models
{
    public class Team
    {
        public int Id { get; set; }
        public int SeasonId { get; set; }
        public string Name { get; set; } = string.Empty; // e.g. "U16", "U18"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // The real IBBA team this maps to - null until a user links one.
        // Many app Teams can point at the same IbbaTeam (e.g. two different
        // parents each tracking their own kid on the same real-world team).
        public int? IbbaTeamId { get; set; }

        // Navigation properties
        public Season Season { get; set; } = null!;
        public IbbaTeam? IbbaTeam { get; set; }
        public ICollection<PlayerTeam> PlayerTeams { get; set; } = new List<PlayerTeam>();
        // No Games collection - Game.HomeTeamId/AwayTeamId aren't a formal FK
        // to this table (they mean IbbaTeam ids for an IBBA-synced game, this
        // table's ids only for a manual one), so EF can't wire up a navigation
        // here; every game lookup goes through GameService's explicit queries.
    }
}
