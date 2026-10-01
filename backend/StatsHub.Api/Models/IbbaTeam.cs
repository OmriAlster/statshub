namespace StatsHub.Api.Models
{
    // One row per real IBBA team - shared across every player and game that
    // touches it, never duplicated per player like the old per-player link
    // table was. Doubles as that team's current league standing (refreshed
    // whenever any team in the same league is synced) and its crest (fetched
    // once, never overwritten once set - re-fetching a logo on every sync
    // isn't worth the extra request).
    public class IbbaTeam
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();

        // Numeric slug id parsed from the team URL (e.g. "151" from
        // /team/151-.../) - the same id space as a game row's
        // HomeTeamCode/AwayTeamCode, so an opponent resolves by id, never by name.
        public string IbbaTeamId { get; set; } = string.Empty;
        public string TeamUrl { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }

        public string LeagueUrl { get; set; } = string.Empty;
        public string LeagueName { get; set; } = string.Empty;
        public int? LeaguePosition { get; set; }
        public int? LeagueTotalTeams { get; set; }
        public int GamesPlayed { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Technical { get; set; }
        public int PointsFor { get; set; }
        public int PointsAgainst { get; set; }
        public int Diff { get; set; }
        public int LeaguePoints { get; set; }
        public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

        // Which StatsHub team(s) map to this real team - the FK lives on Team
        // (Team.IbbaTeamId), not here, because this is the shared side: two
        // different players (each with their own app Team row) can be on the
        // same real IBBA team and both need to link to this same row.
    }
}
