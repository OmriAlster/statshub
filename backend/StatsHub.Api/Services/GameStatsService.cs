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

        // Same rule as GameService: a manual game via its own team, an IBBA
        // game via any team linked to either side - owned by this user's
        // season, or with one of their players on the roster.
        private async Task<bool> CanAccessGameAsync(Game game, int userId)
        {
            if (game.IbbaGameCode == null)
            {
                var teamId = game.HomeTeamId ?? game.AwayTeamId;
                return teamId.HasValue && await _context.Teams.AnyAsync(t =>
                    t.Id == teamId.Value &&
                    (t.Season.UserId == userId || t.PlayerTeams.Any(pt => pt.Player.Parents.Any(pp => pp.UserId == userId))));
            }

            var sideIds = new[] { game.HomeTeamId, game.AwayTeamId }.Where(id => id.HasValue).Select(id => id!.Value).ToList();
            return sideIds.Count > 0 && await _context.Teams.AnyAsync(t =>
                t.IbbaTeamId != null && sideIds.Contains(t.IbbaTeamId.Value) &&
                (t.Season.UserId == userId || t.PlayerTeams.Any(pt => pt.Player.Parents.Any(pp => pp.UserId == userId))));
        }

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

            // The game has to exist and be one this parent can see - without
            // this, any parent could attach their own kid's box score to
            // another family's game by guessing its id (and a missing game
            // crashed on the foreign key instead of saying "not found"). Same
            // 404 for both, so a guessed id doesn't reveal the game exists.
            var game = await _context.Games.FindAsync(dto.GameId);
            if (game == null || !await CanAccessGameAsync(game, requestingUserId))
                throw new KeyNotFoundException("Game not found");

            // One box score per player per game (the database enforces it too,
            // but that surfaced as a crash) - a second one would double-count
            // the game in season stats.
            if (await _context.GameStats.AnyAsync(gs => gs.GameId == dto.GameId && gs.PlayerId == dto.PlayerId))
                throw new InvalidOperationException("This player already has a box score for this game.");

            // A player can only be tracked in one live game at a time - without
            // this, a second "Start Live Game" (different tab, device, or a
            // crashed session that never got cleaned up) would silently create
            // a duplicate in-progress game for the same kid.
            var hasActiveGame = await _context.GameStats
                .AnyAsync(gs => gs.PlayerId == dto.PlayerId && gs.Game.Status == "In Progress");
            if (hasActiveGame)
                throw new InvalidOperationException("This player already has a live game in progress.");

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
            var gameStats = await _context.GameStats.Include(gs => gs.Game).FirstOrDefaultAsync(gs => gs.Id == id);
            if (gameStats == null || !await CanWritePlayerAsync(gameStats.PlayerId, requestingUserId)) return null;

            // Same live-recording lock as GameService.UpdateGameAsync - once
            // someone has claimed a live game, only they can keep writing its
            // box score; everyone else with access just watches.
            var game = gameStats.Game;
            if (game.Status == "In Progress" && game.LiveTrackedByUserId.HasValue && game.LiveTrackedByUserId.Value != requestingUserId)
                return null;

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
            if (dto.OnCourt.HasValue) gameStats.OnCourt = dto.OnCourt.Value;

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

            var jerseyNumber = await _context.PlayerTeams
                .Where(pt => pt.PlayerId == playerId && pt.TeamId == teamId)
                .Select(pt => (int?)pt.JerseyNumber)
                .FirstOrDefaultAsync() ?? 0;

            // HomeTeamId/AwayTeamId mean app Team ids for a manual game
            // (IbbaGameCode null) but IbbaTeam ids for a synced one - a
            // teammate's stats for a shared game need the IbbaTeamId match,
            // not literal team ids, on either side of the fixture.
            var gameStats = await _context.GameStats
                .Where(gs => gs.PlayerId == playerId && gs.Game.GameType != Game.FriendlyGameType && (
                    (gs.Game.IbbaGameCode == null && (gs.Game.HomeTeamId == teamId || gs.Game.AwayTeamId == teamId)) ||
                    (gs.Game.IbbaGameCode != null && team.IbbaTeamId != null && (gs.Game.HomeTeamId == team.IbbaTeamId || gs.Game.AwayTeamId == team.IbbaTeamId))))
                .ToListAsync();

            return BuildTeamStatsDto(player, team, jerseyNumber, gameStats);
        }

        private async Task<List<PlayerTeamStatsDto>> ComputeStatsByPlayerAsync(int playerId)
        {
            // The player comes along with their memberships - no teams means
            // no per-team stat lines to return anyway.
            var teamMemberships = await _context.PlayerTeams
                .Where(pt => pt.PlayerId == playerId)
                .Include(pt => pt.Team)
                .Include(pt => pt.Player)
                .OrderBy(pt => pt.Team.Name)
                .ToListAsync();
            if (teamMemberships.Count == 0) return new List<PlayerTeamStatsDto>();
            var player = teamMemberships[0].Player;

            // One query for this player's box scores across every team, then
            // split per team in memory - instead of one query per team, since
            // each query is a full network round trip to the database in
            // production.
            // Friendly games never count toward season stats.
            var allStats = await _context.GameStats
                .Where(gs => gs.PlayerId == playerId && gs.Game.GameType != Game.FriendlyGameType)
                .Include(gs => gs.Game)
                .ToListAsync();

            var result = new List<PlayerTeamStatsDto>();
            foreach (var membership in teamMemberships)
            {
                var teamId = membership.TeamId;
                var ibbaTeamId = membership.Team.IbbaTeamId;
                var gameStats = allStats
                    .Where(gs =>
                        (gs.Game.IbbaGameCode == null && (gs.Game.HomeTeamId == teamId || gs.Game.AwayTeamId == teamId)) ||
                        (gs.Game.IbbaGameCode != null && ibbaTeamId != null && (gs.Game.HomeTeamId == ibbaTeamId || gs.Game.AwayTeamId == ibbaTeamId)))
                    .ToList();

                result.Add(BuildTeamStatsDto(player, membership.Team, membership.JerseyNumber, gameStats));
            }

            return result;
        }

        private static PlayerTeamStatsDto BuildTeamStatsDto(Player player, Team team, int jerseyNumber, List<GameStats> gameStats)
        {
            var gamesPlayed = gameStats.Count;
            // Every game with a recorded box score counts toward the average,
            // including a real 0-point outing - the alternative (silently
            // excluding it) understates games played and inflates the average.
            // A game the user never actually tracked has no GameStats row at
            // all, so it was never in this list to begin with.
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
                JerseyNumber = jerseyNumber,
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
            OnCourt = gs.OnCourt,
            TotalPoints = gs.TotalPoints
        };
    }
}
