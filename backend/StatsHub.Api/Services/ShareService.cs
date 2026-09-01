using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface IShareService
    {
        Task<ShareLinkDto?> CreateShareLinkAsync(CreateShareLinkDto dto, int requestingUserId);
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

        public async Task<ShareLinkDto?> CreateShareLinkAsync(CreateShareLinkDto dto, int requestingUserId)
        {
            var player = await _context.Players.Include(p => p.Parents).FirstOrDefaultAsync(p => p.Id == dto.PlayerId);
            if (player == null || (player.LinkedUserId != requestingUserId && !player.Parents.Any(pp => pp.UserId == requestingUserId))) return null;

            if (dto.GameId.HasValue)
            {
                // A game is shareable if it's this player's - via team membership
                // (the common case, works even with no box score yet, e.g. an
                // IBBA-synced game nobody has tracked stats for) or via existing
                // GameStats (in case the player has since left that team).
                var game = await _context.Games.FindAsync(dto.GameId.Value);
                if (game == null) return null;

                var isPlayersGame = await _context.PlayerTeams.AnyAsync(pt => pt.PlayerId == dto.PlayerId && pt.TeamId == game.TeamId)
                    || await _context.GameStats.AnyAsync(gs => gs.GameId == dto.GameId && gs.PlayerId == dto.PlayerId);
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
                    .Include(g => g.Team)
                    .Include(g => g.OpponentIbbaTeam)
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Player)
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Shots)
                    .FirstOrDefaultAsync(g => g.Id == link.GameId.Value);

                if (game != null)
                {
                    dto.Game = MapGameToDto(game);
                }
            }
            else
            {
                var stats = await _gameStatsService.GetStatsByPlayerUnrestrictedAsync(link.PlayerId);

                // Crest, league, and standing per team - same IBBA info the signed-in
                // Profiles page shows, resolved directly since a public share link
                // can't go through the authorized IBBA endpoints.
                var ibbaTeams = await _context.PlayerIbbaTeams
                    .Include(pit => pit.PlayerIbbaLink)
                    .Include(pit => pit.IbbaTeam)
                    .Where(pit => pit.PlayerIbbaLink.PlayerId == link.PlayerId && pit.IbbaTeam.LinkedTeamId != null)
                    .Select(pit => pit.IbbaTeam)
                    .ToListAsync();

                dto.Teams = stats.Select(s =>
                {
                    var ibba = ibbaTeams.FirstOrDefault(t => t.LinkedTeamId == s.TeamId);
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
                // from IBBA that has no box score yet still shows up here.
                var teamIds = await _context.PlayerTeams
                    .Where(pt => pt.PlayerId == link.PlayerId)
                    .Select(pt => pt.TeamId)
                    .ToListAsync();
                var statsGameIds = await _context.GameStats
                    .Where(gs => gs.PlayerId == link.PlayerId)
                    .Select(gs => gs.GameId)
                    .ToListAsync();

                var games = await _context.Games
                    .Where(g => teamIds.Contains(g.TeamId) || statsGameIds.Contains(g.Id))
                    .Include(g => g.Team)
                    .Include(g => g.OpponentIbbaTeam)
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Player)
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Shots)
                    .OrderByDescending(g => g.GameDate)
                    .ToListAsync();

                dto.Games = games.Select(MapGameToDto).ToList();
            }

            return dto;
        }

        private static GameDto MapGameToDto(Game game) => new GameDto
        {
            Id = game.Id,
            TeamId = game.TeamId,
            TeamName = game.Team?.Name ?? string.Empty,
            GameType = game.GameType,
            OpponentName = game.OpponentName,
            // Resolved by id at sync time (Game.OpponentIbbaTeamId), never by name -
            // null for a manually-created game, or an opponent not in a synced league.
            OpponentLogoUrl = game.OpponentIbbaTeam?.LogoUrl,
            GameDate = game.GameDate,
            Location = game.Location,
            Status = game.Status,
            TeamScore = game.TeamScore,
            OpponentScore = game.OpponentScore,
            Notes = game.Notes,
            IsHomeGame = game.IsHomeGame,
            IsFromIbba = game.IbbaGameCode != null,
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
