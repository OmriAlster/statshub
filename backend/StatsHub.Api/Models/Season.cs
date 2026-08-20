namespace StatsHub.Api.Models
{
    public class Season
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty; // "2024-2025 Season"
        public string Sport { get; set; } = "Basketball"; // Extensible for other sports
        public int Year { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public User User { get; set; } = null!;
        public ICollection<Team> Teams { get; set; } = new List<Team>();
    }
}
