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
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Player)
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Shots)
                    .FirstOrDefaultAsync(g => g.Id == link.GameId.Value);

                if (game != null)
                {
                    var logos = await BuildOpponentLogoLookupAsync(new[] { game });
                    dto.Game = MapGameToDto(game, logos);
                }
            }
            else
            {
                var stats = await _gameStatsService.GetStatsByPlayerUnrestrictedAsync(link.PlayerId);

                // Crest, league, and standing per team - same IBBA info the signed-in
                // Profiles page shows, resolved directly since a public share link
                // can't go through the authorized IBBA endpoints.
                var ibbaTeams = await _context.IbbaTeamLinks
                    .Include(t => t.PlayerIbbaLink)
                    .Where(t => t.PlayerIbbaLink.PlayerId == link.PlayerId && t.LinkedTeamId != null)
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
                        LogoUrl = ibba?.TeamLogoUrl,
                        IsIbba = ibba != null,
                        LeagueUrl = ibba?.IbbaLeagueUrl,
                        LeagueName = ibba?.IbbaLeagueName,
                    };
                }).ToList();

                if (ibbaTeams.Count > 0)
                {
                    foreach (var team in dto.Teams.Where(t => t.IsIbba && !string.IsNullOrEmpty(t.LeagueUrl)))
                    {
                        var standing = await _context.IbbaStandings
                            .Where(st => st.IbbaLeagueUrl == team.LeagueUrl)
                            .OrderBy(st => st.Position)
                            .ToListAsync();

                        // Match by team URL, not name - a substring name match (e.g.
                        // "מכבי בקה" is contained in "מכבי בקה גת") would silently pick
                        // the wrong row whenever one team's name is a prefix of another's.
                        var ibbaTeam = ibbaTeams.FirstOrDefault(t => t.LinkedTeamId == team.TeamId);
                        var own = !string.IsNullOrEmpty(ibbaTeam?.TeamUrl)
                            ? standing.FirstOrDefault(st => st.TeamUrl == ibbaTeam.TeamUrl)
                            : standing.FirstOrDefault(st => st.TeamName == team.TeamName);
                        if (own != null)
                        {
                            team.StandingPosition = own.Position;
                            team.StandingTotalTeams = standing.Count;
                        }
                    }
                }

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
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Player)
                    .Include(g => g.GameStats.Where(gs => gs.PlayerId == link.PlayerId))
                    .ThenInclude(gs => gs.Shots)
                    .OrderByDescending(g => g.GameDate)
                    .ToListAsync();

                var gameLogos = await BuildOpponentLogoLookupAsync(games);
                dto.Games = games.Select(g => MapGameToDto(g, gameLogos)).ToList();
            }

            return dto;
        }

        // A game's opponent has a logo only when it's also a team in the same
        // synced league's standings - matched by (this game's team's league,
        // opponent name) since a Game only ever stores the opponent as plain
        // text (no team URL of its own to look up directly). Batched across
        // every game passed in, rather than one query per game.
        private async Task<Dictionary<(int TeamId, string OpponentName), string?>> BuildOpponentLogoLookupAsync(IEnumerable<Game> games)
        {
            var result = new Dictionary<(int, string), string?>();
            var teamIds = games.Select(g => g.TeamId).Distinct().ToList();
            if (teamIds.Count == 0) return result;

            var teamLeagues = await _context.IbbaTeamLinks
                .Where(t => t.LinkedTeamId != null && teamIds.Contains(t.LinkedTeamId.Value) && t.IbbaLeagueUrl != null)
                .ToDictionaryAsync(t => t.LinkedTeamId!.Value, t => t.IbbaLeagueUrl!);
            if (teamLeagues.Count == 0) return result;

            var leagueUrls = teamLeagues.Values.Distinct().ToList();
            var standings = await _context.IbbaStandings
                .Where(s => leagueUrls.Contains(s.IbbaLeagueUrl))
                .ToListAsync();
            var teamUrlByLeagueName = standings
                .GroupBy(s => (s.IbbaLeagueUrl, s.TeamName))
                .ToDictionary(g => g.Key, g => g.First().TeamUrl);

            var crestUrls = teamUrlByLeagueName.Values.Distinct().ToList();
            var crests = await _context.IbbaTeamCrests
                .Where(c => crestUrls.Contains(c.TeamUrl))
                .ToDictionaryAsync(c => c.TeamUrl, c => c.LogoUrl);

            foreach (var game in games)
            {
                if (!teamLeagues.TryGetValue(game.TeamId, out var leagueUrl)) continue;
                if (!teamUrlByLeagueName.TryGetValue((leagueUrl, game.OpponentName), out var teamUrl)) continue;
                result[(game.TeamId, game.OpponentName)] = crests.GetValueOrDefault(teamUrl);
            }
            return result;
        }

        private static GameDto MapGameToDto(Game game, Dictionary<(int TeamId, string OpponentName), string?> opponentLogos) => new GameDto
        {
            Id = game.Id,
            TeamId = game.TeamId,
            TeamName = game.Team?.Name ?? string.Empty,
            GameType = game.GameType,
            OpponentName = game.OpponentName,
            OpponentLogoUrl = opponentLogos.GetValueOrDefault((game.TeamId, game.OpponentName)),
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
