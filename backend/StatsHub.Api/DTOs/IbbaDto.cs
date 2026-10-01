namespace StatsHub.Api.DTOs
{
    public class IbbaPreviewDto
    {
        public string PlayerName { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
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

    public class CreatePlayerFromIbbaDto
    {
        public string IbbaPlayerUrl { get; set; } = string.Empty;
    }

    public class CreatePlayerFromIbbaResultDto
    {
        public PlayerDto Player { get; set; } = null!;
        public IbbaLinkStatusDto? Ibba { get; set; }
    }

    public class LinkIbbaTeamDto
    {
        public Guid TeamId { get; set; }
        // The player the pop-up was for - they're added to the team if needed.
        public Guid? PlayerId { get; set; }
    }

    public class CreateTeamForIbbaTeamDto
    {
        public Guid PlayerId { get; set; }
    }

    public class IbbaLinkStatusDto
    {
        public Guid PlayerId { get; set; }
        public string IbbaPlayerUrl { get; set; } = string.Empty;
        public DateTime? LastSyncedAt { get; set; }
        public string? LastSyncError { get; set; }
        // The games and standings are still loading in the background (a link
        // or sync answers as soon as the teams are known).
        public bool GamesLoading { get; set; }
        public List<IbbaTeamLinkDto> Teams { get; set; } = new();
        // The family's teams not linked to any IBBA team - what the pop-up
        // offers to add an IBBA team to, instead of creating a new one.
        public List<TeamDto> ExistingTeams { get; set; } = new();
    }

    public class IbbaTeamLinkDto
    {
        public Guid Id { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string TeamUrl { get; set; } = string.Empty;
        public string? TeamLogoUrl { get; set; }
        public Guid? LinkedTeamId { get; set; }
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
        public string? LogoUrl { get; set; }
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
