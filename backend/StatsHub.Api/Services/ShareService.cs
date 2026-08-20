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
                var gameExists = await _context.GameStats.AnyAsync(gs => gs.GameId == dto.GameId && gs.PlayerId == dto.PlayerId);
                if (!gameExists) return null;
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
                JerseyNumber = player.JerseyNumber,
                Position = player.Position,
                ProfilePictureUrl = player.ProfilePictureUrl
            };

            if (link.GameId.HasValue)
            {
                var game = await _context.Games
                    .Include(g => g.Team)
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Player)
                    .FirstOrDefaultAsync(g => g.Id == link.GameId.Value);

                if (game != null)
                {
                    dto.Game = MapGameToDto(game);
                }
            }
            else
            {
                dto.Teams = await _gameStatsService.GetStatsByPlayerUnrestrictedAsync(link.PlayerId);

                var gameIds = await _context.GameStats
                    .Where(gs => gs.PlayerId == link.PlayerId)
                    .Select(gs => gs.GameId)
                    .ToListAsync();

                var recentGames = await _context.Games
                    .Where(g => gameIds.Contains(g.Id))
                    .Include(g => g.Team)
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Player)
                    .OrderByDescending(g => g.GameDate)
                    .Take(15)
                    .ToListAsync();

                dto.RecentGames = recentGames.Select(MapGameToDto).ToList();
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
            GameDate = game.GameDate,
            Location = game.Location,
            Status = game.Status,
            TeamScore = game.TeamScore,
            OpponentScore = game.OpponentScore,
            Notes = game.Notes,
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
                TotalPoints = gs.TotalPoints
            }).ToList()
        };
    }
}
