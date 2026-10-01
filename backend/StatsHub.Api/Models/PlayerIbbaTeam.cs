namespace StatsHub.Api.Models
{
    // Which IbbaTeams a linked player currently plays for - normally one
    // (their main team), occasionally two (main + a רשאי/loan team). Pure
    // join, no team data duplicated here - that all lives on IbbaTeam itself.
    public class PlayerIbbaTeam
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid PlayerIbbaLinkId { get; set; }
        public Guid IbbaTeamId { get; set; }

        public PlayerIbbaLink PlayerIbbaLink { get; set; } = null!;
        public IbbaTeam IbbaTeam { get; set; } = null!;
    }
}
