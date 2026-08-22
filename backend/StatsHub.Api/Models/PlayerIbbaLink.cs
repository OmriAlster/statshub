namespace StatsHub.Api.Models
{
    public class PlayerIbbaLink
    {
        public int Id { get; set; }
        public int PlayerId { get; set; }
        public string IbbaPlayerUrl { get; set; } = string.Empty;
        public DateTime? LastSyncedAt { get; set; }
        public string? LastSyncError { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Player Player { get; set; } = null!;
        public ICollection<IbbaTeamLink> TeamLinks { get; set; } = new List<IbbaTeamLink>();
    }
}
