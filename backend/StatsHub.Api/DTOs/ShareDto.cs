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
        public int JerseyNumber { get; set; }
        public string Position { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }

        // If set, this share link points at one specific game (box score / live game).
        public GameDto? Game { get; set; }

        // Otherwise, the team-by-team stat summary is shown.
        public List<PlayerTeamStatsDto> Teams { get; set; } = new List<PlayerTeamStatsDto>();
        public List<GameDto> RecentGames { get; set; } = new List<GameDto>();
    }
}
