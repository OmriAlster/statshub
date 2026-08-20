namespace StatsHub.Api.Models
{
    // Roster membership: a player can belong to more than one team in the same
    // season (e.g. a U16 squad and a U18 squad), so this is a many-to-many join.
    public class PlayerTeam
    {
        public int Id { get; set; }
        public int PlayerId { get; set; }
        public int TeamId { get; set; }

        // A player can wear a different number on each team roster.
        public int JerseyNumber { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Player Player { get; set; } = null!;
        public Team Team { get; set; } = null!;
    }
}
