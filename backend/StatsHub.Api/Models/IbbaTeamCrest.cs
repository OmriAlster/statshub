namespace StatsHub.Api.Models
{
    // A cache of team crest URLs, keyed by IBBA team URL - decoupled from
    // IbbaStandings (which gets fully replaced on every sync) so a logo, once
    // found, doesn't need re-fetching from the team's own page on every future
    // sync. Covers any team seen in a standings table, not just ones a player
    // is actually on, so opponents get a crest too.
    public class IbbaTeamCrest
    {
        public int Id { get; set; }
        public string TeamUrl { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    }
}
