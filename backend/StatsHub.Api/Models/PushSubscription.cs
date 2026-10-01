namespace StatsHub.Api.Models
{
    // One row per browser/device a user has enabled notifications on - a
    // parent checking from both a phone and a laptop gets two rows, each
    // pushed to independently.
    public class PushSubscription
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid UserId { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User User { get; set; } = null!;
    }
}
