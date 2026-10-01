using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface IShareService
    {
        Task<ShareLinkDto?> CreateShareLinkAsync(CreateShareLinkDto dto, Guid requestingUserId);
        Task<SharedPlayerDto?> GetByTokenAsync(string token);
    }

    public class ShareService : IShareService
    {
        private readonly AppDbContext _context;
        private readonly IGameStatsService _gameStatsService;

        public ShareService(AppDbContext context, IGameStatsService gameStatsService)
        {
            _context = context;
            _gameStatsService = gameStatsService;
        }

        public async Task<ShareLinkDto?> CreateShareLinkAsync(CreateShareLinkDto dto, Guid requestingUserId)
        {
            var player = await _context.Players.Include(p => p.Parents).FirstOrDefaultAsync(p => p.Id == dto.PlayerId);
            if (player == null || (player.LinkedUserId != requestingUserId && !player.Parents.Any(pp => pp.UserId == requestingUserId))) return null;

            if (dto.GameId.HasValue)
            {
                // A game is shareable if it's this player's - via team membership
                // (the common case, works even with no box score yet, e.g. an
                // IBBA-synced game nobody has tracked stats for), via a shared
                // IbbaTeam on either side of the fixture (a teammate's or an
                // opponent's own app Team, also linked to one of the two real
                // teams, counts too), or via existing GameStats (in case the
                // player has since left that team).
                var game = await _context.Games.FindAsync(dto.GameId.Value);
                if (game == null) return null;

                bool isPlayersGame;
                if (game.IbbaGameCode == null)
                {
                    var manualTeamId = game.HomeTeamId ?? game.AwayTeamId;
                    isPlayersGame = manualTeamId.HasValue && await _context.PlayerTeams.AnyAsync(pt => pt.PlayerId == dto.PlayerId && pt.TeamId == manualTeamId.Value);
                }
                else
                {
                    var sideIds = new[] { game.HomeTeamId, game.AwayTeamId }.Where(id => id.HasValue).Select(id => id!.Value).ToList();
                    isPlayersGame = sideIds.Count > 0 && await _context.PlayerTeams.AnyAsync(pt => pt.PlayerId == dto.PlayerId && pt.Team.IbbaTeamId != null && sideIds.Contains(pt.Team.IbbaTeamId.Value));
                }
                isPlayersGame = isPlayersGame || await _context.GameStats.AnyAsync(gs => gs.GameId == dto.GameId && gs.PlayerId == dto.PlayerId);
                if (!isPlayersGame) return null;
            }

            var link = new ShareLink
            {
                Token = Guid.NewGuid().ToString("N"),
                PlayerId = dto.PlayerId,
                GameId = dto.GameId,
                CreatedByUserId = requestingUserId,
                CreatedAt = DateTime.UtcNow
            };

            _context.ShareLinks.Add(link);
            await _context.SaveChangesAsync();

            return new ShareLinkDto
            {
                Token = link.Token,
                PlayerId = link.PlayerId,
                GameId = link.GameId,
                CreatedAt = link.CreatedAt
            };
        }

        public async Task<SharedPlayerDto?> GetByTokenAsync(string token)
        {
            var link = await _context.ShareLinks
                .Include(sl => sl.Player)
                .FirstOrDefaultAsync(sl => sl.Token == token);

            if (link == null) return null;
            if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTime.UtcNow) return null;

            var player = link.Player;
            var dto = new SharedPlayerDto
            {
                PlayerName = $"{player.FirstName} {player.LastName}",
                Position = player.Position,
                ProfilePictureUrl = player.ProfilePictureUrl
            };

            if (link.GameId.HasValue)
            {
                var game = await _context.Games
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Player)
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Shots)
                    .FirstOrDefaultAsync(g => g.Id == link.GameId.Value);

                if (game != null)
                {
                    // Attribute to this player's own team - the side ids on the
                    // game row may actually belong to a teammate's (or an
                    // opponent's) app Team if their sync created this shared row
                    // first, for an IBBA game.
                    Team? ownTeam;
                    if (game.IbbaGameCode == null)
                    {
                        var manualTeamId = game.HomeTeamId ?? game.AwayTeamId;
                        ownTeam = manualTeamId.HasValue
                            ? await _context.PlayerTeams.Where(pt => pt.PlayerId == link.PlayerId && pt.TeamId == manualTeamId.Value).Include(pt => pt.Team).ThenInclude(t => t.IbbaTeam).Select(pt => pt.Team).FirstOrDefaultAsync()
                            : null;
                    }
                    else
                    {
                        var sideIds = new[] { game.HomeTeamId, game.AwayTeamId }.Where(id => id.HasValue).Select(id => id!.Value).ToList();
                        ownTeam = sideIds.Count > 0
                            ? await _context.PlayerTeams.Where(pt => pt.PlayerId == link.PlayerId && pt.Team.IbbaTeamId != null && sideIds.Contains(pt.Team.IbbaTeamId.Value)).Include(pt => pt.Team).ThenInclude(t => t.IbbaTeam).Select(pt => pt.Team).FirstOrDefaultAsync()
                            : null;
                    }

                    dto.Game = await MapGameToDtoAsync(game, ownTeam);
                }
            }
            else
            {
                var stats = await _gameStatsService.GetStatsByPlayerUnrestrictedAsync(link.PlayerId);

                // Crest, league, and standing per team - same IBBA info the signed-in
                // Profiles page shows, resolved directly since a public share link
                // can't go through the authorized IBBA endpoints. Keyed by the app
                // Team's own IbbaTeamId, not through PlayerIbbaTeams - the link now
                // lives on Team, and a Team's own link is unambiguous regardless of
                // which player is being viewed.
                var statsTeamIds = stats.Select(s => s.TeamId).Distinct().ToList();
                var ibbaByTeamId = await _context.Teams
                    .Include(t => t.IbbaTeam)
                    .Where(t => statsTeamIds.Contains(t.Id) && t.IbbaTeamId != null)
                    .ToDictionaryAsync(t => t.Id, t => t.IbbaTeam!);

                dto.Teams = stats.Select(s =>
                {
                    ibbaByTeamId.TryGetValue(s.TeamId, out var ibba);
                    return new SharedTeamDto
                    {
                        PlayerId = s.PlayerId,
                        PlayerName = s.PlayerName,
                        JerseyNumber = s.JerseyNumber,
                        Position = s.Position,
                        TeamId = s.TeamId,
                        TeamName = s.TeamName,
                        GamesPlayed = s.GamesPlayed,
                        TotalMinutes = s.TotalMinutes,
                        TotalPoints = s.TotalPoints,
                        PointsPerGame = s.PointsPerGame,
                        TotalRebounds = s.TotalRebounds,
                        ReboundsPerGame = s.ReboundsPerGame,
                        TotalAssists = s.TotalAssists,
                        AssistsPerGame = s.AssistsPerGame,
                        TotalSteals = s.TotalSteals,
                        StealsPerGame = s.StealsPerGame,
                        TotalBlocks = s.TotalBlocks,
                        BlocksPerGame = s.BlocksPerGame,
                        TotalTurnovers = s.TotalTurnovers,
                        TurnoversPerGame = s.TurnoversPerGame,
                        FieldGoalPercentage = s.FieldGoalPercentage,
                        ThreePointPercentage = s.ThreePointPercentage,
                        FreeThrowPercentage = s.FreeThrowPercentage,
                        LogoUrl = ibba?.LogoUrl,
                        IsIbba = ibba != null,
                        LeagueUrl = ibba?.LeagueUrl,
                        LeagueName = ibba?.LeagueName,
                        StandingPosition = ibba?.LeaguePosition,
                        StandingTotalTeams = ibba?.LeagueTotalTeams,
                    };
                }).ToList();

                // Every game for a team the player's rostered on, or already has stats
                // for - same rule as the signed-in Games endpoint, so a game synced
                // from IBBA that has no box score yet still shows up here. A shared
                // real-world team can have its game synced under a different
                // player's own app Team, on either side - Home/AwayTeamId (IbbaTeam
                // ids for an IBBA game) are matched, not just literal app Team ids.
                var myTeams = await _context.PlayerTeams
                    .Where(pt => pt.PlayerId == link.PlayerId)
                    .Include(pt => pt.Team)
                    .ThenInclude(t => t.IbbaTeam)
                    .Select(pt => pt.Team)
                    .ToListAsync();
                var teamIds = myTeams.Select(t => t.Id).ToList();
                var teamByIbbaTeamId = myTeams.Where(t => t.IbbaTeamId.HasValue).ToDictionary(t => t.IbbaTeamId!.Value);

                var statsGameIds = await _context.GameStats
                    .Where(gs => gs.PlayerId == link.PlayerId)
                    .Select(gs => gs.GameId)
                    .ToListAsync();

                var games = await _context.Games
                    .Where(g =>
                        (g.IbbaGameCode == null && ((g.HomeTeamId != null && teamIds.Contains(g.HomeTeamId.Value)) || (g.AwayTeamId != null && teamIds.Contains(g.AwayTeamId.Value)))) ||
                        (g.IbbaGameCode != null && ((g.HomeTeamId != null && teamByIbbaTeamId.Keys.Contains(g.HomeTeamId.Value)) || (g.AwayTeamId != null && teamByIbbaTeamId.Keys.Contains(g.AwayTeamId.Value)))) ||
                        statsGameIds.Contains(g.Id))
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Player)
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Shots)
                    .OrderByDescending(g => g.GameDate)
                    .ToListAsync();

                var ibbaTeamIds = games.Where(g => g.IbbaGameCode != null)
                    .SelectMany(g => new[] { g.HomeTeamId, g.AwayTeamId })
                    .Where(id => id.HasValue).Select(id => id!.Value)
                    .Distinct().ToList();
                var ibbaTeams = ibbaTeamIds.Count > 0
                    ? await _context.IbbaTeams.Where(t => ibbaTeamIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id)
                    : new Dictionary<Guid, IbbaTeam>();

                dto.Games = games.Select(g =>
                {
                    Team? viewTeam = null;
                    if (g.IbbaGameCode == null)
                    {
                        var manualTeamId = g.HomeTeamId ?? g.AwayTeamId;
                        viewTeam = myTeams.FirstOrDefault(t => t.Id == manualTeamId);
                    }
                    else
                    {
                        if (g.HomeTeamId.HasValue) teamByIbbaTeamId.TryGetValue(g.HomeTeamId.Value, out viewTeam);
                        if (viewTeam == null && g.AwayTeamId.HasValue) teamByIbbaTeamId.TryGetValue(g.AwayTeamId.Value, out viewTeam);
                    }
                    return MapGameToDto(g, viewTeam, ibbaTeams);
                }).ToList();
            }

            return dto;
        }

        private async Task<GameDto> MapGameToDtoAsync(Game game, Team? viewTeam)
        {
            var ibbaTeams = new Dictionary<Guid, IbbaTeam>();
            if (game.IbbaGameCode != null)
            {
                var sideIds = new[] { game.HomeTeamId, game.AwayTeamId }.Where(id => id.HasValue).Select(id => id!.Value).ToList();
                if (sideIds.Count > 0) ibbaTeams = await _context.IbbaTeams.Where(t => sideIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
            }
            return MapGameToDto(game, viewTeam, ibbaTeams);
        }

        // Same perspective-derivation as GameService.MapToDto - everything
        // relative to "us" (opponent name/logo, our score vs. theirs, home/away)
        // is derived here from the game's objective Home/Away ids compared
        // against viewTeam's own, never stored pre-baked on the row.
        private static GameDto MapGameToDto(Game game, Team? viewTeam, Dictionary<Guid, IbbaTeam> ibbaTeams)
        {
            var isIbba = game.IbbaGameCode != null;
            string opponentName;
            string? opponentLogoUrl;
            int? teamScore, opponentScore;
            bool? isHomeGame;

            if (isIbba)
            {
                bool? viewerIsHome = viewTeam?.IbbaTeamId is Guid viewIbbaId
                    ? (game.HomeTeamId == viewIbbaId ? true : game.AwayTeamId == viewIbbaId ? false : (bool?)null)
                    : null;

                var opponentTeamId = viewerIsHome == false ? game.HomeTeamId : game.AwayTeamId;
                var opponent = opponentTeamId.HasValue && ibbaTeams.TryGetValue(opponentTeamId.Value, out var opp) ? opp : null;

                opponentName = opponent?.Name ?? string.Empty;
                opponentLogoUrl = opponent?.LogoUrl;
                teamScore = viewerIsHome == false ? game.AwayScore : game.HomeScore;
                opponentScore = viewerIsHome == false ? game.HomeScore : game.AwayScore;
                isHomeGame = viewerIsHome;
            }
            else
            {
                opponentName = game.OpponentName;
                opponentLogoUrl = null;
                teamScore = game.TeamScore;
                opponentScore = game.OpponentScore;
                isHomeGame = viewTeam != null
                    ? (game.HomeTeamId == viewTeam.Id ? true : game.AwayTeamId == viewTeam.Id ? false : (bool?)null)
                    : null;
            }

            return new GameDto
            {
                Id = game.Id,
                TeamId = viewTeam?.Id ?? Guid.Empty,
                TeamName = viewTeam?.Name ?? string.Empty,
                TeamLogoUrl = viewTeam?.IbbaTeam?.LogoUrl,
                GameType = game.GameType,
                OpponentName = opponentName,
                OpponentLogoUrl = opponentLogoUrl,
                GameDate = game.GameDate,
                Location = game.Location,
                Status = game.Status,
                TeamScore = teamScore,
                OpponentScore = opponentScore,
                Notes = game.Notes,
                IsHomeGame = isHomeGame,
                IsFromIbba = isIbba,
                CanRecordLive = false, // public share view is always read-only
                PlayerStats = game.GameStats.Select(gs => new GameStatsDto
                {
                    Id = gs.Id,
                    GameId = gs.GameId,
                    PlayerId = gs.PlayerId,
                    PlayerName = $"{gs.Player.FirstName} {gs.Player.LastName}",
                    FieldGoalsMade = gs.FieldGoalsMade,
                    FieldGoalsAttempted = gs.FieldGoalsAttempted,
                    FieldGoalPercentage = gs.FieldGoalPercentage,
                    ThreePointersMade = gs.ThreePointersMade,
                    ThreePointersAttempted = gs.ThreePointersAttempted,
                    ThreePointPercentage = gs.ThreePointPercentage,
                    FreeThrowsMade = gs.FreeThrowsMade,
                    FreeThrowsAttempted = gs.FreeThrowsAttempted,
                    FreeThrowPercentage = gs.FreeThrowPercentage,
                    OffensiveRebounds = gs.OffensiveRebounds,
                    DefensiveRebounds = gs.DefensiveRebounds,
                    TotalRebounds = gs.TotalRebounds,
                    Assists = gs.Assists,
                    Steals = gs.Steals,
                    Blocks = gs.Blocks,
                    Turnovers = gs.Turnovers,
                    Fouls = gs.Fouls,
                    MinutesPlayed = gs.MinutesPlayed,
                    OnCourt = gs.OnCourt,
                    TotalPoints = gs.TotalPoints,
                    Shots = gs.Shots.Select(s => new ShotDto
                    {
                        Id = s.Id,
                        GameStatsId = s.GameStatsId,
                        GameId = gs.GameId,
                        PlayerId = gs.PlayerId,
                        Quarter = s.Quarter,
                        X = s.X,
                        Y = s.Y,
                        Made = s.Made,
                        Value = s.Value
                    }).ToList()
                }).ToList()
            };
        }
    }
}
