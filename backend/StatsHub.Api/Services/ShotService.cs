using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface IShotService
    {
        Task<ShotDto?> CreateShotAsync(CreateShotDto dto, int requestingUserId);
        Task<bool> DeleteShotAsync(int id, int requestingUserId);
        Task<List<ShotDto>> GetShotsByGameStatsAsync(int gameStatsId, int requestingUserId);
        Task<List<ShotDto>> GetShotsByPlayerAndTeamAsync(int playerId, int teamId, int requestingUserId);
    }

    public class ShotService : IShotService
    {
        private readonly AppDbContext _context;

        public ShotService(AppDbContext context)
        {
            _context = context;
        }

        private async Task<bool> CanReadPlayerAsync(int playerId, int userId) =>
            await _context.Players.AnyAsync(p =>
                p.Id == playerId && (p.LinkedUserId == userId || p.Parents.Any(pp => pp.UserId == userId)));

        private Task<bool> CanWritePlayerAsync(int playerId, int userId) =>
            _context.PlayerParents.AnyAsync(pp => pp.PlayerId == playerId && pp.UserId == userId);

        public async Task<ShotDto?> CreateShotAsync(CreateShotDto dto, int requestingUserId)
        {
            var gameStats = await _context.GameStats.FindAsync(dto.GameStatsId);
            if (gameStats == null || !await CanWritePlayerAsync(gameStats.PlayerId, requestingUserId)) return null;

            var shot = new Shot
            {
                GameStatsId = dto.GameStatsId,
                Quarter = dto.Quarter,
                X = dto.X,
                Y = dto.Y,
                Made = dto.Made,
                Value = dto.Value,
                CreatedAt = DateTime.UtcNow
            };
            _context.Shots.Add(shot);

            ApplyShotToGameStats(gameStats, dto.Value, dto.Made, 1);
            gameStats.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return await MapToDtoAsync(shot);
        }

        public async Task<bool> DeleteShotAsync(int id, int requestingUserId)
        {
            var shot = await _context.Shots.FindAsync(id);
            if (shot == null) return false;

            var gameStats = await _context.GameStats.FindAsync(shot.GameStatsId);
            if (gameStats == null || !await CanWritePlayerAsync(gameStats.PlayerId, requestingUserId)) return false;

            ApplyShotToGameStats(gameStats, shot.Value, shot.Made, -1);
            gameStats.UpdatedAt = DateTime.UtcNow;

            _context.Shots.Remove(shot);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<ShotDto>> GetShotsByGameStatsAsync(int gameStatsId, int requestingUserId)
        {
            var gameStats = await _context.GameStats.FindAsync(gameStatsId);
            if (gameStats == null || !await CanReadPlayerAsync(gameStats.PlayerId, requestingUserId)) return new List<ShotDto>();

            var shots = await _context.Shots
                .Where(s => s.GameStatsId == gameStatsId)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();

            return shots.Select(s => MapToDto(s, gameStats)).ToList();
        }

        public async Task<List<ShotDto>> GetShotsByPlayerAndTeamAsync(int playerId, int teamId, int requestingUserId)
        {
            if (!await CanReadPlayerAsync(playerId, requestingUserId)) return new List<ShotDto>();

            var shots = await _context.Shots
                .Where(s => s.GameStats.PlayerId == playerId && s.GameStats.Game.TeamId == teamId)
                .Include(s => s.GameStats)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();

            return shots.Select(s => MapToDto(s, s.GameStats)).ToList();
        }

        // direction: +1 when adding a shot, -1 when removing one
        private static void ApplyShotToGameStats(GameStats gameStats, int value, bool made, int direction)
        {
            if (value == 3)
            {
                gameStats.ThreePointersAttempted = Math.Max(0, gameStats.ThreePointersAttempted + direction);
                if (made) gameStats.ThreePointersMade = Math.Max(0, gameStats.ThreePointersMade + direction);
            }
            else
            {
                gameStats.FieldGoalsAttempted = Math.Max(0, gameStats.FieldGoalsAttempted + direction);
                if (made) gameStats.FieldGoalsMade = Math.Max(0, gameStats.FieldGoalsMade + direction);
            }
        }

        private async Task<ShotDto> MapToDtoAsync(Shot shot)
        {
            var gameStats = await _context.GameStats.FindAsync(shot.GameStatsId);
            return MapToDto(shot, gameStats!);
        }

        private static ShotDto MapToDto(Shot shot, GameStats gameStats) => new ShotDto
        {
            Id = shot.Id,
            GameStatsId = shot.GameStatsId,
            GameId = gameStats.GameId,
            PlayerId = gameStats.PlayerId,
            Quarter = shot.Quarter,
            X = shot.X,
            Y = shot.Y,
            Made = shot.Made,
            Value = shot.Value
        };
    }
}
