namespace StatsHub.Api.Models
{
    public class PlayerIbbaLink
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid PlayerId { get; set; }
        public string IbbaPlayerUrl { get; set; } = string.Empty;
        public DateTime? LastSyncedAt { get; set; }
        public string? LastSyncError { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Player Player { get; set; } = null!;
        public ICollection<PlayerIbbaTeam> Teams { get; set; } = new List<PlayerIbbaTeam>();
    }
}
