using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface IGameStatsService
    {
        Task<GameStatsDto?> GetGameStatsByIdAsync(int id, int requestingUserId);
        Task<GameStatsDto> CreateGameStatsAsync(CreateGameStatsDto dto, int requestingUserId);
        Task<GameStatsDto?> UpdateGameStatsAsync(int id, UpdateGameStatsDto dto, int requestingUserId);
        Task<bool> DeleteGameStatsAsync(int id, int requestingUserId);
        Task<List<PlayerTeamStatsDto>> GetStatsByPlayerAsync(int playerId, int requestingUserId);
        Task<PlayerTeamStatsDto?> GetTeamStatsForPlayerAsync(int playerId, int teamId, int requestingUserId);
        Task<List<PlayerTeamStatsDto>> GetStatsByPlayerUnrestrictedAsync(int playerId);
    }

    public class GameStatsService : IGameStatsService
    {
        private readonly AppDbContext _context;

        public GameStatsService(AppDbContext context)
        {
            _context = context;
        }

        private async Task<bool> CanReadPlayerAsync(int playerId, int userId) =>
            await _context.Players.AnyAsync(p =>
                p.Id == playerId && (p.LinkedUserId == userId || p.Parents.Any(pp => pp.UserId == userId)));

        // Any linked parent (not just the one who created the player) can record stats.
        private Task<bool> CanWritePlayerAsync(int playerId, int userId) =>
            _context.PlayerParents.AnyAsync(pp => pp.PlayerId == playerId && pp.UserId == userId);

        public async Task<GameStatsDto?> GetGameStatsByIdAsync(int id, int requestingUserId)
        {
            var stats = await _context.GameStats
                .Include(gs => gs.Player)
                .FirstOrDefaultAsync(gs => gs.Id == id);

            if (stats == null || !await CanReadPlayerAsync(stats.PlayerId, requestingUserId)) return null;

            return MapToDto(stats);
        }

        public async Task<GameStatsDto> CreateGameStatsAsync(CreateGameStatsDto dto, int requestingUserId)
        {
            if (!await CanWritePlayerAsync(dto.PlayerId, requestingUserId))
                throw new UnauthorizedAccessException("Player not found or not owned by user");

            var gameStats = new GameStats
            {
                GameId = dto.GameId,
                PlayerId = dto.PlayerId,
                FieldGoalsMade = dto.FieldGoalsMade,
                FieldGoalsAttempted = dto.FieldGoalsAttempted,
                ThreePointersMade = dto.ThreePointersMade,
                ThreePointersAttempted = dto.ThreePointersAttempted,
                FreeThrowsMade = dto.FreeThrowsMade,
                FreeThrowsAttempted = dto.FreeThrowsAttempted,
                OffensiveRebounds = dto.OffensiveRebounds,
                DefensiveRebounds = dto.DefensiveRebounds,
                Assists = dto.Assists,
                Steals = dto.Steals,
                Blocks = dto.Blocks,
                Turnovers = dto.Turnovers,
                Fouls = dto.Fouls,
                MinutesPlayed = dto.MinutesPlayed,
                CreatedAt = DateTime.UtcNow
            };

            _context.GameStats.Add(gameStats);
            await _context.SaveChangesAsync();

            return await GetGameStatsByIdAsync(gameStats.Id, requestingUserId) ?? new GameStatsDto();
        }

        public async Task<GameStatsDto?> UpdateGameStatsAsync(int id, UpdateGameStatsDto dto, int requestingUserId)
        {
            var gameStats = await _context.GameStats.FindAsync(id);
            if (gameStats == null || !await CanWritePlayerAsync(gameStats.PlayerId, requestingUserId)) return null;

            if (dto.FieldGoalsMade.HasValue) gameStats.FieldGoalsMade = dto.FieldGoalsMade.Value;
            if (dto.FieldGoalsAttempted.HasValue) gameStats.FieldGoalsAttempted = dto.FieldGoalsAttempted.Value;
            if (dto.ThreePointersMade.HasValue) gameStats.ThreePointersMade = dto.ThreePointersMade.Value;
            if (dto.ThreePointersAttempted.HasValue) gameStats.ThreePointersAttempted = dto.ThreePointersAttempted.Value;
            if (dto.FreeThrowsMade.HasValue) gameStats.FreeThrowsMade = dto.FreeThrowsMade.Value;
            if (dto.FreeThrowsAttempted.HasValue) gameStats.FreeThrowsAttempted = dto.FreeThrowsAttempted.Value;
            if (dto.OffensiveRebounds.HasValue) gameStats.OffensiveRebounds = dto.OffensiveRebounds.Value;
            if (dto.DefensiveRebounds.HasValue) gameStats.DefensiveRebounds = dto.DefensiveRebounds.Value;
            if (dto.Assists.HasValue) gameStats.Assists = dto.Assists.Value;
            if (dto.Steals.HasValue) gameStats.Steals = dto.Steals.Value;
            if (dto.Blocks.HasValue) gameStats.Blocks = dto.Blocks.Value;
            if (dto.Turnovers.HasValue) gameStats.Turnovers = dto.Turnovers.Value;
            if (dto.Fouls.HasValue) gameStats.Fouls = dto.Fouls.Value;
            if (dto.MinutesPlayed.HasValue) gameStats.MinutesPlayed = dto.MinutesPlayed.Value;

            gameStats.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return await GetGameStatsByIdAsync(id, requestingUserId);
        }

        public async Task<bool> DeleteGameStatsAsync(int id, int requestingUserId)
        {
            var gameStats = await _context.GameStats.FindAsync(id);
            if (gameStats == null || !await CanWritePlayerAsync(gameStats.PlayerId, requestingUserId)) return false;

            _context.GameStats.Remove(gameStats);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<PlayerTeamStatsDto>> GetStatsByPlayerAsync(int playerId, int requestingUserId)
        {
            if (!await CanReadPlayerAsync(playerId, requestingUserId)) return new List<PlayerTeamStatsDto>();
            return await ComputeStatsByPlayerAsync(playerId);
        }

        public async Task<List<PlayerTeamStatsDto>> GetStatsByPlayerUnrestrictedAsync(int playerId)
        {
            return await ComputeStatsByPlayerAsync(playerId);
        }

        public async Task<PlayerTeamStatsDto?> GetTeamStatsForPlayerAsync(int playerId, int teamId, int requestingUserId)
        {
            if (!await CanReadPlayerAsync(playerId, requestingUserId)) return null;

            var player = await _context.Players.FindAsync(playerId);
            var team = await _context.Teams.FindAsync(teamId);
            if (player == null || team == null) return null;

            var gameStats = await _context.GameStats
                .Where(gs => gs.PlayerId == playerId && gs.Game.TeamId == teamId)
                .ToListAsync();

            return BuildTeamStatsDto(player, team, gameStats);
        }

        private async Task<List<PlayerTeamStatsDto>> ComputeStatsByPlayerAsync(int playerId)
        {
            var player = await _context.Players.FindAsync(playerId);
            if (player == null) return new List<PlayerTeamStatsDto>();

            var teams = await _context.Teams
                .Where(t => t.PlayerTeams.Any(pt => pt.PlayerId == playerId))
                .OrderBy(t => t.Name)
                .ToListAsync();

            var result = new List<PlayerTeamStatsDto>();
            foreach (var team in teams)
            {
                var gameStats = await _context.GameStats
                    .Where(gs => gs.PlayerId == playerId && gs.Game.TeamId == team.Id)
                    .ToListAsync();

                result.Add(BuildTeamStatsDto(player, team, gameStats));
            }

            return result;
        }

        private static PlayerTeamStatsDto BuildTeamStatsDto(Player player, Team team, List<GameStats> gameStats)
        {
            var gamesPlayed = gameStats.Count;
            double PerGame(int total) => gamesPlayed > 0 ? Math.Round((double)total / gamesPlayed, 2) : 0;

            var totalPoints = gameStats.Sum(gs => gs.TotalPoints);
            var totalRebounds = gameStats.Sum(gs => gs.TotalRebounds);
            var totalAssists = gameStats.Sum(gs => gs.Assists);
            var totalSteals = gameStats.Sum(gs => gs.Steals);
            var totalBlocks = gameStats.Sum(gs => gs.Blocks);
            var totalTurnovers = gameStats.Sum(gs => gs.Turnovers);

            var totalFGA = gameStats.Sum(gs => gs.FieldGoalsAttempted);
            var totalThreePA = gameStats.Sum(gs => gs.ThreePointersAttempted);
            var totalFTA = gameStats.Sum(gs => gs.FreeThrowsAttempted);

            return new PlayerTeamStatsDto
            {
                PlayerId = player.Id,
                PlayerName = $"{player.FirstName} {player.LastName}",
                JerseyNumber = player.JerseyNumber,
                Position = player.Position,
                TeamId = team.Id,
                TeamName = team.Name,
                GamesPlayed = gamesPlayed,
                TotalMinutes = gameStats.Sum(gs => gs.MinutesPlayed),
                TotalPoints = totalPoints,
                PointsPerGame = PerGame(totalPoints),
                TotalRebounds = totalRebounds,
                ReboundsPerGame = PerGame(totalRebounds),
                TotalAssists = totalAssists,
                AssistsPerGame = PerGame(totalAssists),
                TotalSteals = totalSteals,
                StealsPerGame = PerGame(totalSteals),
                TotalBlocks = totalBlocks,
                BlocksPerGame = PerGame(totalBlocks),
                TotalTurnovers = totalTurnovers,
                TurnoversPerGame = PerGame(totalTurnovers),
                FieldGoalPercentage = totalFGA > 0 ? Math.Round((double)gameStats.Sum(gs => gs.FieldGoalsMade) / totalFGA * 100, 2) : 0,
                ThreePointPercentage = totalThreePA > 0 ? Math.Round((double)gameStats.Sum(gs => gs.ThreePointersMade) / totalThreePA * 100, 2) : 0,
                FreeThrowPercentage = totalFTA > 0 ? Math.Round((double)gameStats.Sum(gs => gs.FreeThrowsMade) / totalFTA * 100, 2) : 0
            };
        }

        private static GameStatsDto MapToDto(GameStats gs) => new GameStatsDto
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
        };
    }
}
