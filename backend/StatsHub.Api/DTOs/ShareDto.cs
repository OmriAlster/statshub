namespace StatsHub.Api.DTOs
{
    public class CreateShareLinkDto
    {
        public int PlayerId { get; set; }
        public int? GameId { get; set; }
    }

    public class ShareLinkDto
    {
        public string Token { get; set; } = string.Empty;
        public int PlayerId { get; set; }
        public int? GameId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SharedPlayerDto
    {
        public string PlayerName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }

        // If set, this share link points at one specific game (box score / live game).
        public GameDto? Game { get; set; }

        // Otherwise, the full profile is shown: every team (with crest/league/standing,
        // same as the signed-in Profiles page) and every game, not just a recent few.
        public List<SharedTeamDto> Teams { get; set; } = new List<SharedTeamDto>();
        public List<GameDto> Games { get; set; } = new List<GameDto>();
    }

    public class SharedTeamDto : PlayerTeamStatsDto
    {
        public string? LogoUrl { get; set; }
        public bool IsIbba { get; set; }
        public string? LeagueUrl { get; set; }
        public string? LeagueName { get; set; }
        public int? StandingPosition { get; set; }
        public int? StandingTotalTeams { get; set; }
    }
}
