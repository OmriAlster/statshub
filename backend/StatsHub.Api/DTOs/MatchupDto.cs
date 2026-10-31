namespace StatsHub.Api.DTOs
{
    // A game's opponent before it's played: where both sides stand in this
    // season's table, and this season's earlier games between them.
    public class MatchupDto
    {
        public MatchupTeamDto? Opponent { get; set; }
        public MatchupTeamDto? Team { get; set; }
        public List<HeadToHeadGameDto> Previous { get; set; } = new();
    }

    public class MatchupTeamDto
    {
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public string? LeagueName { get; set; }
        public int Position { get; set; }
        public int? TotalTeams { get; set; }
        public int GamesPlayed { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int PointsFor { get; set; }
        public int PointsAgainst { get; set; }
    }

    public class HeadToHeadGameDto
    {
        public Guid GameId { get; set; }
        public DateTime GameDate { get; set; }
        public string GameType { get; set; } = string.Empty;
        public bool? IsHomeGame { get; set; }
        public int? TeamScore { get; set; }
        public int? OpponentScore { get; set; }
        // The player's line in that game, when it was recorded.
        public int? Points { get; set; }
        public int? Rebounds { get; set; }
        public int? Assists { get; set; }
    }
}
