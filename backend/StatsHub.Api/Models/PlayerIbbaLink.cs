namespace StatsHub.Api.Models
{
    public class PlayerIbbaLink
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid PlayerId { get; set; }
        public string IbbaPlayerUrl { get; set; } = string.Empty;

        // Which league the player is linked to (ILeagueProvider.Key) - named
        // Ibba* from when IBBA was the only one.
        public string Provider { get; set; } = "ibba";
        // The player's code on scoresheets, saved at the first sheet their
        // name was found on - later sheets find them by it.
        public string? ScoresheetCode { get; set; }
        public DateTime? LastSyncedAt { get; set; }
        public string? LastSyncError { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Player Player { get; set; } = null!;
        public ICollection<PlayerIbbaTeam> Teams { get; set; } = new List<PlayerIbbaTeam>();
    }
}
