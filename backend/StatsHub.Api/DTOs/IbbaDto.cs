namespace StatsHub.Api.DTOs
{
    public class IbbaPreviewDto
    {
        public string PlayerName { get; set; } = string.Empty;
        public List<IbbaPreviewTeamDto> Teams { get; set; } = new();
    }

    public class IbbaPreviewTeamDto
    {
        public string TeamName { get; set; } = string.Empty;
    }

    public class LinkIbbaPlayerDto
    {
        public string IbbaPlayerUrl { get; set; } = string.Empty;
    }

    public class LinkIbbaTeamDto
    {
        public int TeamId { get; set; }
    }

    public class IbbaLinkStatusDto
    {
        public int PlayerId { get; set; }
        public string IbbaPlayerUrl { get; set; } = string.Empty;
        public DateTime? LastSyncedAt { get; set; }
        public string? LastSyncError { get; set; }
        public List<IbbaTeamLinkDto> Teams { get; set; } = new();
    }

    public class IbbaTeamLinkDto
    {
        public int Id { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string TeamUrl { get; set; } = string.Empty;
        public string? TeamLogoUrl { get; set; }
        public int? LinkedTeamId { get; set; }
        public string? LinkedTeamName { get; set; }
        public string? IbbaLeagueUrl { get; set; }
        public string? IbbaLeagueName { get; set; }
        public int? Position { get; set; }
        public int? TotalTeams { get; set; }
    }

    public class IbbaStandingDto
    {
        public int Position { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string TeamUrl { get; set; } = string.Empty;
        public int GamesPlayed { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Technical { get; set; }
        public int PointsFor { get; set; }
        public int PointsAgainst { get; set; }
        public int Diff { get; set; }
        public int LeaguePoints { get; set; }
    }
}
