namespace StatsHub.Api.DTOs
{
    public class GameDto
    {
        public int Id { get; set; }
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        // The viewing team's own crest - only populated when that app Team is
        // itself linked to an IbbaTeam, regardless of whether this particular
        // game is IBBA-synced or manual (the crest belongs to the team, not
        // the game).
        public string? TeamLogoUrl { get; set; }
        public string GameType { get; set; } = "League";
        public string OpponentName { get; set; } = string.Empty;
        // Only ever populated for an IBBA-synced game whose opponent is also a
        // team in the same synced league's standings - there's no logo source
        // for a manually-created game's opponent.
        public string? OpponentLogoUrl { get; set; }
        public DateTime GameDate { get; set; }
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int? TeamScore { get; set; }
        public int? OpponentScore { get; set; }
        public string? Notes { get; set; }
        public bool? IsHomeGame { get; set; }
        public bool IsFromIbba { get; set; }
        // True unless someone else is already live-tracking this game -
        // whoever's live-tracking it is the only one who can keep recording
        // (Game and GameStats writes both enforce this server-side too, this
        // is what the frontend uses to decide whether to show the editor or
        // a read-only "watching" view).
        public bool CanRecordLive { get; set; } = true;
        public List<GameStatsDto> PlayerStats { get; set; } = new List<GameStatsDto>();
    }

    public class CreateGameDto
    {
        public int TeamId { get; set; }
        public string GameType { get; set; } = "League";
        public string OpponentName { get; set; } = string.Empty;
        public DateTime GameDate { get; set; }
        public string Location { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public class UpdateGameDto
    {
        public string? OpponentName { get; set; }
        public DateTime? GameDate { get; set; }
        public string? Location { get; set; }
        public string? Status { get; set; }
        public string? GameType { get; set; }
        public int? TeamScore { get; set; }
        public int? OpponentScore { get; set; }
        public string? Notes { get; set; }
        public bool? IsHomeGame { get; set; }
    }
}
