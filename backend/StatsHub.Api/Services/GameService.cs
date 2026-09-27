using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface IGameService
    {
        Task<List<GameDto>> GetGamesByTeamAsync(int teamId, int requestingUserId);
        // viewingPlayerId: the player whose page the game is shown on - see
        // FindAccessibleOwnTeamAsync.
        Task<GameDto?> GetGameByIdAsync(int id, int requestingUserId, int? viewingPlayerId = null);
        Task<List<GameDto>> GetGamesByPlayerAsync(int playerId, int requestingUserId);
        Task<GameDto> CreateGameAsync(CreateGameDto dto, int requestingUserId);
        Task<GameDto?> UpdateGameAsync(int id, UpdateGameDto dto, int requestingUserId, int? viewingPlayerId = null);
        Task<bool> DeleteGameAsync(int id, int requestingUserId);

        // Called right before a Team is deleted (player removal, or a direct
        // team delete) - removes only the games that are exclusively this
        // team's; a shared IBBA game with another still-linked Team on either
        // side (same real team, or the opponent) is left alone.
        Task DeleteGamesExclusiveToTeamAsync(int teamId);
    }

    public class GameService : IGameService
    {
        private readonly AppDbContext _context;
        private readonly IPushNotificationService _push;

        public GameService(AppDbContext context, IPushNotificationService push)
        {
            _context = context;
            _push = push;
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

        // HomeTeamId/AwayTeamId mean different tables depending on the game:
        // for a manual game (IbbaGameCode null) they're app Team ids, at most
        // one set; for an IBBA game they're IbbaTeam ids on both sides, and any
        // app Team linked to either side (a different player's own team, on
        // either side of the fixture) can access it.
        // Several app Teams can link to the same IbbaTeam (two siblings each
        // with their own team, a co-parent's team, an old duplicate) - and
        // they can even be on opposite sides of the fixture. Picking "the
        // first accessible one" returned an arbitrary team: a game reloaded
        // on its own came back labelled with a different team than the one
        // on screen, so a team-filtered Schedule dropped it (it looked
        // deleted after closing Edit Game), and home/away and the score
        // could be read from the wrong side. When the caller says which
        // player's page it's on, that player's own team wins; otherwise the
        // lowest id, so the pick is at least stable.
        private async Task<Team?> FindAccessibleOwnTeamAsync(Game game, int requestingUserId, int? viewingPlayerId = null)
        {
            if (game.IbbaGameCode == null)
            {
                var teamId = game.HomeTeamId ?? game.AwayTeamId;
                if (!teamId.HasValue) return null;

                return await _context.Teams.Include(t => t.IbbaTeam).FirstOrDefaultAsync(t =>
                    t.Id == teamId.Value && (
                        t.Season.UserId == requestingUserId ||
                        t.PlayerTeams.Any(pt => pt.Player.Parents.Any(pp => pp.UserId == requestingUserId))
                    ));
            }

            var sideIds = new[] { game.HomeTeamId, game.AwayTeamId }
                .Where(id => id.HasValue).Select(id => id!.Value).ToList();
            if (sideIds.Count == 0) return null;

            return await _context.Teams.Include(t => t.IbbaTeam)
                .Where(t =>
                    t.IbbaTeamId != null && sideIds.Contains(t.IbbaTeamId.Value) && (
                        t.Season.UserId == requestingUserId ||
                        t.PlayerTeams.Any(pt => pt.Player.Parents.Any(pp => pp.UserId == requestingUserId))
                    ))
                .OrderByDescending(t => viewingPlayerId != null && t.PlayerTeams.Any(pt => pt.PlayerId == viewingPlayerId))
                .ThenBy(t => t.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<List<GameDto>> GetGamesByTeamAsync(int teamId, int requestingUserId)
        {
            if (!await OwnsTeamAsync(teamId, requestingUserId)) return new List<GameDto>();

            var team = await _context.Teams.Include(t => t.IbbaTeam).FirstAsync(t => t.Id == teamId);

            var games = await _context.Games
                .Where(g =>
                    (g.IbbaGameCode == null && (g.HomeTeamId == teamId || g.AwayTeamId == teamId)) ||
                    (g.IbbaGameCode != null && team.IbbaTeamId != null && (g.HomeTeamId == team.IbbaTeamId || g.AwayTeamId == team.IbbaTeamId)))
                .Include(g => g.GameStats)
                .ThenInclude(gs => gs.Player)
                .OrderByDescending(g => g.GameDate)
                .ToListAsync();

            // Every game here is being viewed through this specific team, so it
            // should present as this team's game regardless of which app Team's
            // sync actually created the underlying row.
            return await MapGamesToDtoAsync(games, _ => team, requestingUserId);
        }

        public async Task<GameDto?> GetGameByIdAsync(int id, int requestingUserId, int? viewingPlayerId = null)
        {
            var game = await _context.Games
                .Include(g => g.GameStats)
                .ThenInclude(gs => gs.Player)
                .Include(g => g.GameStats)
                .ThenInclude(gs => gs.Shots)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (game == null) return null;

            var ownTeam = await FindAccessibleOwnTeamAsync(game, requestingUserId, viewingPlayerId);
            if (ownTeam == null)
            {
                // Not a team owner - only reachable via a stats-only fallback
                // (e.g. the player has since left every relevant team), so
                // there's no real "viewing team" context. Best-effort name for
                // a manual game via its stored side id; an IBBA game just
                // shows without a "my team" framing at all.
                var hasStats = await _context.GameStats.AnyAsync(gs => gs.GameId == game.Id && gs.Player.LinkedUserId == requestingUserId);
                if (!hasStats) return null;

                Team? fallbackTeam = null;
                if (game.IbbaGameCode == null)
                {
                    var fallbackTeamId = game.HomeTeamId ?? game.AwayTeamId;
                    if (fallbackTeamId.HasValue) fallbackTeam = await _context.Teams.Include(t => t.IbbaTeam).FirstOrDefaultAsync(t => t.Id == fallbackTeamId.Value);
                }

                var fallbackDtos = await MapGamesToDtoAsync(new List<Game> { game }, _ => fallbackTeam, requestingUserId);
                return fallbackDtos[0];
            }

            var dtos = await MapGamesToDtoAsync(new List<Game> { game }, _ => ownTeam, requestingUserId);
            return dtos[0];
        }

        public async Task<List<GameDto>> GetGamesByPlayerAsync(int playerId, int requestingUserId)
        {
            // A player's games are every game for a team they're currently rostered
            // on - not just games they already have stats for. That distinction used
            // to be invisible because every Game was created together with its
            // GameStats via live tracking, but a game synced from IBBA has no stats
            // until the user adds them, and would otherwise never show up here.
            // Stats-only games are kept too, in case a player has left the team since.
            //
            // The access check rides along on the team lookup (every query is a
            // full network round trip to the database in production) - only a
            // player with no teams at all needs it checked separately.
            var myTeams = await _context.PlayerTeams
                .Where(pt => pt.PlayerId == playerId &&
                    (pt.Player.LinkedUserId == requestingUserId || pt.Player.Parents.Any(pp => pp.UserId == requestingUserId)))
                .Include(pt => pt.Team)
                .ThenInclude(t => t.IbbaTeam)
                .Select(pt => pt.Team)
                .ToListAsync();

            if (myTeams.Count == 0)
            {
                var canAccess = await _context.Players.AnyAsync(p =>
                    p.Id == playerId && (p.LinkedUserId == requestingUserId || p.Parents.Any(pp => pp.UserId == requestingUserId)));
                if (!canAccess) return new List<GameDto>();
            }
            var teamIds = myTeams.Select(t => t.Id).ToList();
            // Maps a shared IbbaTeam back to whichever of THIS player's own teams
            // is linked to it, so a game synced under a different player's app
            // Team (same real IbbaTeam, either side) still resolves to this
            // player's own team.
            var teamByIbbaTeamId = myTeams.Where(t => t.IbbaTeamId.HasValue).ToDictionary(t => t.IbbaTeamId!.Value);

            var ibbaTeamIds = teamByIbbaTeamId.Keys.ToList();
            var games = await _context.Games
                .Where(g =>
                    (g.IbbaGameCode == null && ((g.HomeTeamId != null && teamIds.Contains(g.HomeTeamId.Value)) || (g.AwayTeamId != null && teamIds.Contains(g.AwayTeamId.Value)))) ||
                    (g.IbbaGameCode != null && ((g.HomeTeamId != null && ibbaTeamIds.Contains(g.HomeTeamId.Value)) || (g.AwayTeamId != null && ibbaTeamIds.Contains(g.AwayTeamId.Value)))) ||
                    // Stats-only games, as a subquery instead of a separate round trip.
                    g.GameStats.Any(gs => gs.PlayerId == playerId))
                .Include(g => g.GameStats.Where(gs => gs.PlayerId == playerId))
                .ThenInclude(gs => gs.Player)
                .OrderByDescending(g => g.GameDate)
                .ToListAsync();

            Team? ResolveViewTeam(Game g)
            {
                if (g.IbbaGameCode == null)
                {
                    var teamId = g.HomeTeamId ?? g.AwayTeamId;
                    return myTeams.FirstOrDefault(t => t.Id == teamId);
                }

                Team? found = null;
                if (g.HomeTeamId.HasValue) teamByIbbaTeamId.TryGetValue(g.HomeTeamId.Value, out found);
                if (found == null && g.AwayTeamId.HasValue) teamByIbbaTeamId.TryGetValue(g.AwayTeamId.Value, out found);
                return found;
            }

            return await MapGamesToDtoAsync(games, ResolveViewTeam, requestingUserId);
        }

        public async Task<GameDto> CreateGameAsync(CreateGameDto dto, int requestingUserId)
        {
            if (!await OwnsTeamAsync(dto.TeamId, requestingUserId))
                throw new UnauthorizedAccessException("Team not found or not owned by user");

            // No home/away preference is given at creation time, so this
            // defaults to home - editable afterward via UpdateGameDto.IsHomeGame,
            // which moves the id between Home/AwayTeamId rather than flipping a
            // separately-stored flag.
            var game = new Game
            {
                HomeTeamId = dto.TeamId,
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

            // Fire-and-forget-ish, but awaited so a slow push provider doesn't
            // silently drop errors - NotifyTeamAsync itself never throws for
            // per-subscription failures, only for genuinely unexpected ones.
            await _push.NotifyTeamAsync(
                dto.TeamId,
                "🏀 New game scheduled",
                $"vs {game.OpponentName} on {{datetime}}",
                $"/games/{game.Id}",
                excludeUserId: requestingUserId,
                gameDate: game.GameDate);

            return await GetGameByIdAsync(game.Id, requestingUserId) ?? throw new InvalidOperationException("Game was not created");
        }

        public async Task<GameDto?> UpdateGameAsync(int id, UpdateGameDto dto, int requestingUserId, int? viewingPlayerId = null)
        {
            var game = await _context.Games.FindAsync(id);
            if (game == null) return null;
            var ownTeam = await FindAccessibleOwnTeamAsync(game, requestingUserId, viewingPlayerId);
            if (ownTeam == null) return null;

            // Whoever is already live-tracking this game is the only one who can
            // keep recording - everyone else with access just watches (read-only,
            // like the public share-player view) until it leaves "In Progress".
            if (game.Status == "In Progress" && game.LiveTrackedByUserId.HasValue && game.LiveTrackedByUserId.Value != requestingUserId)
                return null;

            var previousStatus = game.Status;
            var isIbbaGame = game.IbbaGameCode != null;

            // Schedule facts (opponent, date/time, location, type, home/away) for an
            // IBBA-synced game only ever come from IBBA - the Schedule tab already
            // hides the form that would edit them, and the IBBA sync itself now keeps
            // them current on every resync. This is the backstop: even a direct API
            // call can't fight the next sync. Status/scores stay editable regardless
            // of source, since live-tracking a game to completion (via the Live Game
            // widget) uses this same endpoint for IBBA-synced games too.
            if (!isIbbaGame)
            {
                if (!string.IsNullOrEmpty(dto.OpponentName)) game.OpponentName = dto.OpponentName;
                if (dto.GameDate.HasValue) game.GameDate = dto.GameDate.Value;
                if (!string.IsNullOrEmpty(dto.Location)) game.Location = dto.Location;
                if (!string.IsNullOrEmpty(dto.GameType)) game.GameType = dto.GameType;
                if (dto.IsHomeGame.HasValue)
                {
                    // Moves the team id between slots instead of storing a
                    // separate home/away flag alongside it.
                    if (dto.IsHomeGame.Value) { game.HomeTeamId = ownTeam.Id; game.AwayTeamId = null; }
                    else { game.AwayTeamId = ownTeam.Id; game.HomeTeamId = null; }
                }

                if (dto.TeamScore.HasValue) game.TeamScore = dto.TeamScore;
                if (dto.OpponentScore.HasValue) game.OpponentScore = dto.OpponentScore;
            }
            else
            {
                // The score coming in is always phrased as "us vs. them" from the
                // live-tracking client's point of view - translate that into the
                // objective Home/Away columns based on which side ownTeam is on.
                var viewerIsHome = game.HomeTeamId == ownTeam.IbbaTeamId;
                if (dto.TeamScore.HasValue)
                {
                    if (viewerIsHome) game.HomeScore = dto.TeamScore; else game.AwayScore = dto.TeamScore;
                }
                if (dto.OpponentScore.HasValue)
                {
                    if (viewerIsHome) game.AwayScore = dto.OpponentScore; else game.HomeScore = dto.OpponentScore;
                }
            }
            if (!string.IsNullOrEmpty(dto.Status)) game.Status = dto.Status;
            if (!string.IsNullOrEmpty(dto.Notes)) game.Notes = dto.Notes;

            // Claim recording rights on the way into "In Progress" (first writer
            // wins, so a second parent racing to start the same game doesn't
            // steal it back); release them once it's no longer live, so normal
            // multi-parent editing resumes.
            if (game.Status == "In Progress" && !game.LiveTrackedByUserId.HasValue)
                game.LiveTrackedByUserId = requestingUserId;
            else if (game.Status != "In Progress")
                game.LiveTrackedByUserId = null;

            game.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await NotifyOnStatusChangeAsync(game, ownTeam, previousStatus, requestingUserId);

            return await GetGameByIdAsync(id, requestingUserId, viewingPlayerId);
        }

        // Live-tracking milestones - game start and final score - not every
        // point scored along the way, which would be far too noisy for a push
        // notification. Excludes whoever just made the change themselves, since
        // the person tracking the game obviously already knows. An IBBA-synced
        // game notifies every app Team linked to its shared IbbaTeam on the
        // acting side (everyone tracking a kid on that real team); a
        // manually-created game is always single-team, so ownTeam is right there.
        private async Task NotifyOnStatusChangeAsync(Game game, Team ownTeam, string previousStatus, int actingUserId)
        {
            if (previousStatus == game.Status) return;

            var isIbbaGame = game.IbbaGameCode != null;

            Task Notify(string title, string body) => isIbbaGame && ownTeam.IbbaTeamId.HasValue
                ? _push.NotifyIbbaTeamAsync(ownTeam.IbbaTeamId.Value, title, body, $"/games/{game.Id}", excludeUserId: actingUserId)
                : _push.NotifyTeamAsync(ownTeam.Id, title, body, $"/games/{game.Id}", excludeUserId: actingUserId);

            if (game.Status == "In Progress")
            {
                var opponentName = await ResolveOpponentNameAsync(game, ownTeam);
                await Notify("🔴 Live now", $"vs {opponentName} has started");
            }
            else if (game.Status == "Completed")
            {
                var opponentName = await ResolveOpponentNameAsync(game, ownTeam);
                var (teamScore, opponentScore) = ResolvePerspectiveScores(game, ownTeam);
                var result = (teamScore ?? 0) > (opponentScore ?? 0) ? "W" : "L";
                await Notify("Final score", $"{result} {teamScore}-{opponentScore} vs {opponentName}");
            }
        }

        private async Task<string> ResolveOpponentNameAsync(Game game, Team ownTeam)
        {
            if (game.IbbaGameCode == null) return game.OpponentName;

            var opponentTeamId = game.HomeTeamId == ownTeam.IbbaTeamId ? game.AwayTeamId : game.HomeTeamId;
            if (!opponentTeamId.HasValue) return string.Empty;

            var opponent = await _context.IbbaTeams.FindAsync(opponentTeamId.Value);
            return opponent?.Name ?? string.Empty;
        }

        private static (int? TeamScore, int? OpponentScore) ResolvePerspectiveScores(Game game, Team ownTeam)
        {
            if (game.IbbaGameCode == null) return (game.TeamScore, game.OpponentScore);

            return game.HomeTeamId == ownTeam.IbbaTeamId
                ? (game.HomeScore, game.AwayScore)
                : (game.AwayScore, game.HomeScore);
        }

        public async Task<bool> DeleteGameAsync(int id, int requestingUserId)
        {
            var game = await _context.Games.FindAsync(id);
            if (game == null) return false;
            if (await FindAccessibleOwnTeamAsync(game, requestingUserId) == null) return false;

            _context.Games.Remove(game);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task DeleteGamesExclusiveToTeamAsync(int teamId)
        {
            var team = await _context.Teams.FindAsync(teamId);
            if (team == null) return;

            // Manually-created games are exclusively this team's - nobody else
            // could ever see them, so they're always removed together with it.
            var manualGames = await _context.Games
                .Where(g => g.IbbaGameCode == null && (g.HomeTeamId == teamId || g.AwayTeamId == teamId))
                .ToListAsync();
            _context.Games.RemoveRange(manualGames);

            if (team.IbbaTeamId.HasValue)
            {
                var ibbaTeamId = team.IbbaTeamId.Value;
                var sharedGames = await _context.Games
                    .Where(g => g.IbbaGameCode != null && (g.HomeTeamId == ibbaTeamId || g.AwayTeamId == ibbaTeamId))
                    .ToListAsync();

                foreach (var game in sharedGames)
                {
                    var sideIds = new[] { game.HomeTeamId, game.AwayTeamId }
                        .Where(sideId => sideId.HasValue).Select(sideId => sideId!.Value).ToList();

                    // Another app Team - anyone's, not just this user's, since the
                    // point is a *different family* on the same real team, or on
                    // the opponent's, should never lose the game - still linked to
                    // either side of this fixture means someone else still needs it.
                    var stillNeeded = await _context.Teams.AnyAsync(t =>
                        t.Id != teamId && t.IbbaTeamId != null && sideIds.Contains(t.IbbaTeamId.Value));

                    if (!stillNeeded) _context.Games.Remove(game);
                }
            }

            await _context.SaveChangesAsync();
        }

        // Batches the IbbaTeam lookups every IBBA game in the list needs (both
        // sides, since a single column can't carry a real FK to two different
        // tables - see Game.cs), then maps each game with its viewing team
        // resolved via resolveViewTeam. Everything perspective-relative
        // (opponent name/logo, "my" score vs. theirs, is this a home game) is
        // derived per-game from comparing the fixture's objective Home/Away ids
        // against the viewing team's own - nothing on the Game row itself
        // privileges one side.
        private async Task<List<GameDto>> MapGamesToDtoAsync(List<Game> games, Func<Game, Team?> resolveViewTeam, int requestingUserId)
        {
            var ibbaTeamIds = games
                .Where(g => g.IbbaGameCode != null)
                .SelectMany(g => new[] { g.HomeTeamId, g.AwayTeamId })
                .Where(id => id.HasValue).Select(id => id!.Value)
                .Distinct().ToList();

            var ibbaTeams = ibbaTeamIds.Count > 0
                ? await _context.IbbaTeams.Where(t => ibbaTeamIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id)
                : new Dictionary<int, IbbaTeam>();

            return games.Select(g => MapToDto(g, resolveViewTeam(g), ibbaTeams, requestingUserId)).ToList();
        }

        private static GameDto MapToDto(Game game, Team? viewTeam, Dictionary<int, IbbaTeam> ibbaTeams, int requestingUserId)
        {
            var isIbba = game.IbbaGameCode != null;
            string opponentName;
            string? opponentLogoUrl;
            int? teamScore, opponentScore;
            bool? isHomeGame;

            if (isIbba)
            {
                bool? viewerIsHome = viewTeam?.IbbaTeamId is int viewIbbaId
                    ? (game.HomeTeamId == viewIbbaId ? true : game.AwayTeamId == viewIbbaId ? false : (bool?)null)
                    : null;

                // Defaults to the "home" framing both when the viewer really is
                // home, and when their side couldn't be determined at all (no
                // better guess available for that rare fallback).
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
                opponentLogoUrl = null; // manual games never had opponent logos
                teamScore = game.TeamScore;
                opponentScore = game.OpponentScore;
                isHomeGame = viewTeam != null
                    ? (game.HomeTeamId == viewTeam.Id ? true : game.AwayTeamId == viewTeam.Id ? false : (bool?)null)
                    : null;
            }

            return new GameDto
            {
                Id = game.Id,
                TeamId = viewTeam?.Id ?? 0,
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
                CanRecordLive = !game.LiveTrackedByUserId.HasValue || game.LiveTrackedByUserId.Value == requestingUserId,
                PlayerStats = game.GameStats.Select(MapGameStatsToDto).ToList()
            };
        }

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
        };
    }
}
