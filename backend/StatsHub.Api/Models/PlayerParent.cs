namespace StatsHub.Api.Models
{
    // A player can have more than one parent/guardian account with full
    // access (e.g. mom + dad both tracking the same kid).
    public class PlayerParent
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid PlayerId { get; set; }
        public Guid UserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Player Player { get; set; } = null!;
        public User User { get; set; } = null!;
    }
}
