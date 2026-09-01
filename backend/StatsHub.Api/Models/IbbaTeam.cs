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
        public int Id { get; set; }

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

        // Which StatsHub team this maps to - null until a user links one.
        // Lives on the team itself (not per-player) since it's the same real
        // team regardless of which of your players happens to be on it.
        public int? LinkedTeamId { get; set; }

        public Team? LinkedTeam { get; set; }
    }
}
