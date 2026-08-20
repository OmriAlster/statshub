namespace StatsHub.Api.Models
{
    public class Shot
    {
        public int Id { get; set; }
        public int GameStatsId { get; set; }
        public int Quarter { get; set; }

        // Court position as a fraction of the half-court (0..1 on both axes),
        // so it's independent of whatever pixel size the court is drawn at.
        public double X { get; set; }
        public double Y { get; set; }

        public bool Made { get; set; }
        public int Value { get; set; } // 2 or 3, derived from court position when taken

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public GameStats GameStats { get; set; } = null!;
    }
}
