namespace StatsHub.Api.Models
{
    // One row per team in a league's standings table. Shared across every
    // player/team linked to that league - refreshed wholesale (delete + reinsert
    // all rows for the league) on each sync rather than per-player.
    public class IbbaStanding
    {
        public int Id { get; set; }
        public string IbbaLeagueUrl { get; set; } = string.Empty;
        public string IbbaLeagueName { get; set; } = string.Empty;
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
        public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    }
}
