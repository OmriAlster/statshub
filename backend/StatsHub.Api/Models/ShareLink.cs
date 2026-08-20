namespace StatsHub.Api.Models
{
    public class ShareLink
    {
        public int Id { get; set; }
        public string Token { get; set; } = string.Empty;
        public int PlayerId { get; set; }
        public int? GameId { get; set; } // null = share whole player profile/season, set = share a single game
        public int CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }

        // Navigation properties
        public Player Player { get; set; } = null!;
        public Game? Game { get; set; }
    }
}
