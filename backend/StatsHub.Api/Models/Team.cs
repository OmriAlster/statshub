namespace StatsHub.Api.Models
{
    public class Team
    {
        public int Id { get; set; }
        public int SeasonId { get; set; }
        public string Name { get; set; } = string.Empty; // e.g. "U16", "U18"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Season Season { get; set; } = null!;
        public ICollection<PlayerTeam> PlayerTeams { get; set; } = new List<PlayerTeam>();
        public ICollection<Game> Games { get; set; } = new List<Game>();
    }
}
