using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface IGameService
    {
        Task<List<GameDto>> GetGamesByTeamAsync(int teamId, int requestingUserId);
        Task<GameDto?> GetGameByIdAsync(int id, int requestingUserId);
        Task<List<GameDto>> GetGamesByPlayerAsync(int playerId, int requestingUserId);
        Task<GameDto> CreateGameAsync(CreateGameDto dto, int requestingUserId);
        Task<GameDto?> UpdateGameAsync(int id, UpdateGameDto dto, int requestingUserId);
        Task<bool> DeleteGameAsync(int id, int requestingUserId);
    }

    public class GameService : IGameService
    {
        private readonly AppDbContext _context;

        public GameService(AppDbContext context)
        {
            _context = context;
        }

        // A team is manageable by its season owner, or by any parent of a player
        // on that team's roster - so a second parent who was invited onto one of
        // their kid's teams can also create/edit games for that team.
        private async Task<bool> OwnsTeamAsync(int teamId, int requestingUserId) =>
            await _context.Teams.AnyAsync(t =>
                t.Id == teamId && (
                    t.Season.UserId == requestingUserId ||
                    t.PlayerTeams.Any(pt => pt.Player.Parents.Any(pp => pp.UserId == requestingUserId))
                ));

        private async Task<bool> CanAccessGameAsync(Game game, int requestingUserId)
        {
            if (await OwnsTeamAsync(game.TeamId, requestingUserId)) return true;

            return await _context.GameStats.AnyAsync(gs => gs.GameId == game.Id && gs.Player.LinkedUserId == requestingUserId);
        }

        public async Task<List<GameDto>> GetGamesByTeamAsync(int teamId, int requestingUserId)
        {
            if (!await OwnsTeamAsync(teamId, requestingUserId)) return new List<GameDto>();

            var games = await _context.Games
                .Where(g => g.TeamId == teamId)
                .Include(g => g.Team)
                .Include(g => g.GameStats)
                .ThenInclude(gs => gs.Player)
                .OrderByDescending(g => g.GameDate)
                .ToListAsync();

            return games.Select(MapToDto).ToList();
        }

        public async Task<GameDto?> GetGameByIdAsync(int id, int requestingUserId)
        {
            var game = await _context.Games
                .Include(g => g.Team)
                .Include(g => g.GameStats)
                .ThenInclude(gs => gs.Player)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (game == null || !await CanAccessGameAsync(game, requestingUserId)) return null;

            return MapToDto(game);
        }

        public async Task<List<GameDto>> GetGamesByPlayerAsync(int playerId, int requestingUserId)
        {
            var canAccess = await _context.Players.AnyAsync(p =>
                p.Id == playerId && (p.LinkedUserId == requestingUserId || p.Parents.Any(pp => pp.UserId == requestingUserId)));
            if (!canAccess) return new List<GameDto>();

            var gameIds = await _context.GameStats
                .Where(gs => gs.PlayerId == playerId)
                .Select(gs => gs.GameId)
                .ToListAsync();

            var games = await _context.Games
                .Where(g => gameIds.Contains(g.Id))
                .Include(g => g.Team)
                .Include(g => g.GameStats.Where(gs => gs.PlayerId == playerId))
                .ThenInclude(gs => gs.Player)
                .OrderByDescending(g => g.GameDate)
                .ToListAsync();

            return games.Select(MapToDto).ToList();
        }

        public async Task<GameDto> CreateGameAsync(CreateGameDto dto, int requestingUserId)
        {
            if (!await OwnsTeamAsync(dto.TeamId, requestingUserId))
                throw new UnauthorizedAccessException("Team not found or not owned by user");

            if (dto.GameDate.Date > DateTime.UtcNow.Date)
                throw new InvalidOperationException("Games are recorded live and can't be created with a future date.");

            var game = new Game
            {
                TeamId = dto.TeamId,
                GameType = string.IsNullOrWhiteSpace(dto.GameType) ? "League" : dto.GameType,
                OpponentName = dto.OpponentName,
                GameDate = dto.GameDate,
                Location = dto.Location,
                Status = "Upcoming",
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _context.Games.Add(game);
            await _context.SaveChangesAsync();

            return await GetGameByIdAsync(game.Id, requestingUserId) ?? throw new InvalidOperationException("Game was not created");
        }

        public async Task<GameDto?> UpdateGameAsync(int id, UpdateGameDto dto, int requestingUserId)
        {
            var game = await _context.Games.FindAsync(id);
            if (game == null) return null;
            if (!await OwnsTeamAsync(game.TeamId, requestingUserId)) return null;

            if (!string.IsNullOrEmpty(dto.OpponentName)) game.OpponentName = dto.OpponentName;
            if (dto.GameDate.HasValue) game.GameDate = dto.GameDate.Value;
            if (!string.IsNullOrEmpty(dto.Location)) game.Location = dto.Location;
            if (!string.IsNullOrEmpty(dto.Status)) game.Status = dto.Status;
            if (!string.IsNullOrEmpty(dto.GameType)) game.GameType = dto.GameType;
            if (dto.TeamScore.HasValue) game.TeamScore = dto.TeamScore;
            if (dto.OpponentScore.HasValue) game.OpponentScore = dto.OpponentScore;
            if (!string.IsNullOrEmpty(dto.Notes)) game.Notes = dto.Notes;

            game.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return await GetGameByIdAsync(id, requestingUserId);
        }

        public async Task<bool> DeleteGameAsync(int id, int requestingUserId)
        {
            var game = await _context.Games.FindAsync(id);
            if (game == null) return false;
            if (!await OwnsTeamAsync(game.TeamId, requestingUserId)) return false;

            _context.Games.Remove(game);
            await _context.SaveChangesAsync();
            return true;
        }

        private static GameDto MapToDto(Game game) => new GameDto
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
            PlayerStats = game.GameStats.Select(MapGameStatsToDto).ToList()
        };

        private static GameStatsDto MapGameStatsToDto(GameStats gs) => new GameStatsDto
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
