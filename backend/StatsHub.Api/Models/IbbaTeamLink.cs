namespace StatsHub.Api.Models
{
    // One row per team a linked player is found under on IBBA - normally one
    // (their main team), occasionally two (main + a רשאי/loan team).
    public class IbbaTeamLink
    {
        public int Id { get; set; }
        public int PlayerIbbaLinkId { get; set; }
        public string IbbaTeamSlugId { get; set; } = string.Empty;
        public string IbbaTeamExportId { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
        public string TeamUrl { get; set; } = string.Empty;
        public string? TeamLogoUrl { get; set; }

        // Which StatsHub team this maps to - null until the user links/creates
        // a matching team. Games can only be synced once this is set.
        public int? LinkedTeamId { get; set; }

        public string? IbbaLeagueUrl { get; set; }
        public string? IbbaLeagueName { get; set; }

        // Navigation properties
        public PlayerIbbaLink PlayerIbbaLink { get; set; } = null!;
        public Team? LinkedTeam { get; set; }
        public ICollection<Game> Games { get; set; } = new List<Game>();
    }
}
