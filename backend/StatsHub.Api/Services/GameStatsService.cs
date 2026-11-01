using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface IGameStatsService
    {
        Task<GameStatsDto?> GetGameStatsByIdAsync(Guid id, Guid requestingUserId);
        Task<GameStatsDto> CreateGameStatsAsync(CreateGameStatsDto dto, Guid requestingUserId);
        Task<GameStatsDto?> UpdateGameStatsAsync(Guid id, UpdateGameStatsDto dto, Guid requestingUserId);
        Task<bool> DeleteGameStatsAsync(Guid id, Guid requestingUserId);
        Task<GameStatsDto> StatsFromScoresheetAsync(StatsFromScoresheetDto dto, Guid requestingUserId);
        Task<List<PlayerTeamStatsDto>> GetStatsByPlayerAsync(Guid playerId, Guid requestingUserId, string? gameType = null);
        Task<PlayerTeamStatsDto?> GetTeamStatsForPlayerAsync(Guid playerId, Guid teamId, Guid requestingUserId);
        Task<List<PlayerTeamStatsDto>> GetStatsByPlayerUnrestrictedAsync(Guid playerId);
    }

    public class GameStatsService : IGameStatsService
    {
        private readonly AppDbContext _context;

        public GameStatsService(AppDbContext context)
        {
            _context = context;
        }

        // See AccessRules.
        private Task<bool> CanReadPlayerAsync(Guid playerId, Guid userId) => _context.CanReadPlayerAsync(playerId, userId);
        private Task<bool> CanWritePlayerAsync(Guid playerId, Guid userId) => _context.CanWritePlayerAsync(playerId, userId);
        private Task<bool> CanAccessGameAsync(Game game, Guid userId) => _context.CanAccessGameAsync(game, userId);

        public async Task<GameStatsDto?> GetGameStatsByIdAsync(Guid id, Guid requestingUserId)
        {
            var stats = await _context.GameStats
                .Include(gs => gs.Player)
                .FirstOrDefaultAsync(gs => gs.Id == id);

            if (stats == null || !await CanReadPlayerAsync(stats.PlayerId, requestingUserId)) return null;

            return stats.ToDto();
        }

        public async Task<GameStatsDto> CreateGameStatsAsync(CreateGameStatsDto dto, Guid requestingUserId)
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

        public async Task<GameStatsDto?> UpdateGameStatsAsync(Guid id, UpdateGameStatsDto dto, Guid requestingUserId)
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
            // Edited by the parent - theirs now; a sync never changes it again.
            if (HasStatChange(dto)) gameStats.Source = null;

            gameStats.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return await GetGameStatsByIdAsync(id, requestingUserId);
        }

        // Back to IBBA's numbers: the game's stats (and their shot chart, which
        // the sheet's makes wouldn't match) replaced by the scoresheet's line -
        // updated by the sync again from then on, like any line from a sheet.
        public async Task<GameStatsDto> StatsFromScoresheetAsync(StatsFromScoresheetDto dto, Guid requestingUserId)
        {
            if (!await CanWritePlayerAsync(dto.PlayerId, requestingUserId))
                throw new UnauthorizedAccessException("Player not found or not owned by user");
            var game = await _context.Games.FindAsync(dto.GameId);
            if (game == null || !await CanAccessGameAsync(game, requestingUserId))
                throw new KeyNotFoundException("Game not found");
            if (game.Status == "In Progress")
                throw new InvalidOperationException("The game is being tracked live.");
            var line = await _context.ScoresheetLines.FirstOrDefaultAsync(l => l.GameId == dto.GameId && l.PlayerId == dto.PlayerId)
                ?? throw new KeyNotFoundException("This game has no scoresheet line for the player.");

            var existing = await _context.GameStats.FirstOrDefaultAsync(gs => gs.GameId == dto.GameId && gs.PlayerId == dto.PlayerId);
            if (existing != null)
            {
                _context.GameStats.Remove(existing);
                await _context.SaveChangesAsync();
            }
            var stats = ScoresheetStatsService.FromSheet(new GameStats { GameId = dto.GameId, PlayerId = dto.PlayerId }, line);
            _context.GameStats.Add(stats);
            await _context.SaveChangesAsync();

            var result = (await GetGameStatsByIdAsync(stats.Id, requestingUserId))!;
            result.Scoresheet = line.ToDto();
            return result;
        }

        private static bool HasStatChange(UpdateGameStatsDto dto) =>
            new int?[] { dto.FieldGoalsMade, dto.FieldGoalsAttempted, dto.ThreePointersMade, dto.ThreePointersAttempted, dto.FreeThrowsMade,
                dto.FreeThrowsAttempted, dto.OffensiveRebounds, dto.DefensiveRebounds, dto.Assists, dto.Steals, dto.Blocks, dto.Turnovers,
                dto.Fouls, dto.MinutesPlayed }.Any(v => v.HasValue);

        public async Task<bool> DeleteGameStatsAsync(Guid id, Guid requestingUserId)
        {
            var gameStats = await _context.GameStats.FindAsync(id);
            if (gameStats == null || !await CanWritePlayerAsync(gameStats.PlayerId, requestingUserId)) return false;

            _context.GameStats.Remove(gameStats);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<PlayerTeamStatsDto>> GetStatsByPlayerAsync(Guid playerId, Guid requestingUserId, string? gameType = null)
        {
            if (!await CanReadPlayerAsync(playerId, requestingUserId)) return new List<PlayerTeamStatsDto>();
            return await ComputeStatsByPlayerAsync(playerId, Game.ParseGameTypeFilter(gameType));
        }

        public async Task<List<PlayerTeamStatsDto>> GetStatsByPlayerUnrestrictedAsync(Guid playerId)
        {
            return await ComputeStatsByPlayerAsync(playerId);
        }

        public async Task<PlayerTeamStatsDto?> GetTeamStatsForPlayerAsync(Guid playerId, Guid teamId, Guid requestingUserId)
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

        private async Task<List<PlayerTeamStatsDto>> ComputeStatsByPlayerAsync(Guid playerId, string? gameType = null)
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
            // Friendly games never count toward season stats - unless
            // friendlies are exactly what was asked for.
            var allStats = await _context.GameStats
                .Where(gs => gs.PlayerId == playerId && (gameType == null ? gs.Game.GameType != Game.FriendlyGameType : gs.Game.GameType == gameType))
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
            // all, so it was never in this list to begin with. A stat that
            // wasn't recorded in a game (null) is averaged over the games
            // that have it instead.
            static double? PerGame(int total, int games) => games == 0 ? null : Math.Round((double)total / games, 2);
            (int? Total, double? PerGame) Stat(Func<GameStats, int?> stat)
            {
                var known = gameStats.Select(stat).OfType<int>().ToList();
                return known.Count == 0 ? (null, null) : (known.Sum(), PerGame(known.Sum(), known.Count));
            }
            // Made shots out of attempts, from the games where the attempts were recorded.
            double? Percentage(Func<GameStats, int> made, Func<GameStats, int?> attempted)
            {
                var known = gameStats.Where(gs => attempted(gs) is not null).ToList();
                return known.Count == 0 ? null : GameStats.Percentage(known.Sum(made), known.Sum(gs => attempted(gs)!.Value));
            }

            var totalPoints = gameStats.Sum(gs => gs.TotalPoints);
            var rebounds = Stat(gs => gs.TotalRebounds);
            var assists = Stat(gs => gs.Assists);
            var steals = Stat(gs => gs.Steals);
            var blocks = Stat(gs => gs.Blocks);
            var turnovers = Stat(gs => gs.Turnovers);

            return new PlayerTeamStatsDto
            {
                PlayerId = player.Id,
                PlayerName = $"{player.FirstName} {player.LastName}",
                JerseyNumber = jerseyNumber,
                Position = player.Position,
                TeamId = team.Id,
                TeamName = team.Name,
                GamesPlayed = gamesPlayed,
                TotalMinutes = Stat(gs => gs.MinutesPlayed).Total,
                TotalPoints = totalPoints,
                PointsPerGame = PerGame(totalPoints, gamesPlayed),
                TotalRebounds = rebounds.Total,
                ReboundsPerGame = rebounds.PerGame,
                TotalAssists = assists.Total,
                AssistsPerGame = assists.PerGame,
                TotalSteals = steals.Total,
                StealsPerGame = steals.PerGame,
                TotalBlocks = blocks.Total,
                BlocksPerGame = blocks.PerGame,
                TotalTurnovers = turnovers.Total,
                TurnoversPerGame = turnovers.PerGame,
                FieldGoalPercentage = Percentage(gs => gs.FieldGoalsMade, gs => gs.FieldGoalsAttempted),
                ThreePointPercentage = Percentage(gs => gs.ThreePointersMade, gs => gs.ThreePointersAttempted),
                FreeThrowPercentage = Percentage(gs => gs.FreeThrowsMade, gs => gs.FreeThrowsAttempted)
            };
        }
    }
}
