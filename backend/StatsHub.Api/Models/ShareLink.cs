namespace StatsHub.Api.Models
{
    public class ShareLink
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public string Token { get; set; } = string.Empty;
        public Guid PlayerId { get; set; }
        public Guid? GameId { get; set; } // null = share whole player profile/season, set = share a single game
        public Guid CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }

        // Navigation properties
        public Player Player { get; set; } = null!;
        public Game? Game { get; set; }
    }
}
