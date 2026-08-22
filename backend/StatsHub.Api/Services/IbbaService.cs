using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.IbbaScraping;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface IIbbaService
    {
        Task<IbbaPreviewDto> PreviewAsync(string playerUrl);
        Task<IbbaLinkStatusDto?> LinkPlayerAsync(int playerId, string ibbaPlayerUrl, int requestingUserId);
        Task<bool> UnlinkPlayerAsync(int playerId, int requestingUserId);
        Task<IbbaLinkStatusDto?> SyncPlayerAsync(int playerId, int requestingUserId);
        Task<IbbaLinkStatusDto?> GetLinkStatusAsync(int playerId, int requestingUserId);
        Task<IbbaLinkStatusDto?> LinkTeamAsync(int ibbaTeamLinkId, int teamId, int requestingUserId);
        Task<List<IbbaStandingDto>> GetStandingsAsync(string leagueUrl);
    }

    public class IbbaService : IIbbaService
    {
        private readonly AppDbContext _context;
        private readonly IPlayerService _playerService;
        private readonly IHttpClientFactory _httpClientFactory;

        public IbbaService(AppDbContext context, IPlayerService playerService, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _playerService = playerService;
            _httpClientFactory = httpClientFactory;
        }

        private HttpClient CreateIbbaHttpClient() => _httpClientFactory.CreateClient("Ibba");

        private async Task<bool> OwnsTeamAsync(int teamId, int requestingUserId) =>
            await _context.Teams.AnyAsync(t =>
                t.Id == teamId && (
                    t.Season.UserId == requestingUserId ||
                    t.PlayerTeams.Any(pt => pt.Player.Parents.Any(pp => pp.UserId == requestingUserId))
                ));

        public async Task<IbbaPreviewDto> PreviewAsync(string playerUrl)
        {
            var scraper = new IbbaPlayerScraper(CreateIbbaHttpClient());
            var info = await scraper.GetPlayerInfoAsync(playerUrl);

            if (info.Teams.Count == 0)
                throw new InvalidOperationException("Could not find any current team for this player on the IBBA page. Check the URL is a player profile page.");

            return new IbbaPreviewDto
            {
                PlayerName = info.PlayerName,
                Teams = info.Teams.Select(t => new IbbaPreviewTeamDto { TeamName = t.TeamName }).ToList()
            };
        }

        public async Task<IbbaLinkStatusDto?> LinkPlayerAsync(int playerId, string ibbaPlayerUrl, int requestingUserId)
        {
            if (!await _playerService.CanAccessPlayerAsync(playerId, requestingUserId)) return null;

            var link = await _context.PlayerIbbaLinks.FirstOrDefaultAsync(l => l.PlayerId == playerId);
            if (link == null)
            {
                link = new PlayerIbbaLink { PlayerId = playerId, IbbaPlayerUrl = ibbaPlayerUrl, CreatedAt = DateTime.UtcNow };
                _context.PlayerIbbaLinks.Add(link);
            }
            else
            {
                link.IbbaPlayerUrl = ibbaPlayerUrl;
            }
            await _context.SaveChangesAsync();

            await RunSyncAsync(link);
            return await GetLinkStatusAsync(playerId, requestingUserId);
        }

        public async Task<bool> UnlinkPlayerAsync(int playerId, int requestingUserId)
        {
            if (!await _playerService.CanAccessPlayerAsync(playerId, requestingUserId)) return false;

            var link = await _context.PlayerIbbaLinks.FirstOrDefaultAsync(l => l.PlayerId == playerId);
            if (link == null) return false;

            // Detach real, stats-bearing games from the team link before it's deleted -
            // unlinking from IBBA must never touch a game's own data, only the
            // attribution of where it came from.
            var teamLinkIds = await _context.IbbaTeamLinks
                .Where(t => t.PlayerIbbaLinkId == link.Id)
                .Select(t => t.Id)
                .ToListAsync();
            if (teamLinkIds.Count > 0)
            {
                var affectedGames = await _context.Games
                    .Where(g => g.IbbaTeamLinkId != null && teamLinkIds.Contains(g.IbbaTeamLinkId.Value))
                    .ToListAsync();
                foreach (var game in affectedGames) game.IbbaTeamLinkId = null;
            }

            _context.PlayerIbbaLinks.Remove(link);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IbbaLinkStatusDto?> SyncPlayerAsync(int playerId, int requestingUserId)
        {
            if (!await _playerService.CanAccessPlayerAsync(playerId, requestingUserId)) return null;

            var link = await _context.PlayerIbbaLinks.FirstOrDefaultAsync(l => l.PlayerId == playerId);
            if (link == null) return null;

            await RunSyncAsync(link);
            return await GetLinkStatusAsync(playerId, requestingUserId);
        }

        public async Task<IbbaLinkStatusDto?> GetLinkStatusAsync(int playerId, int requestingUserId)
        {
            if (!await _playerService.CanAccessPlayerAsync(playerId, requestingUserId)) return null;

            var link = await _context.PlayerIbbaLinks
                .Include(l => l.TeamLinks)
                .ThenInclude(t => t.LinkedTeam)
                .FirstOrDefaultAsync(l => l.PlayerId == playerId);
            if (link == null) return null;

            var teamDtos = new List<IbbaTeamLinkDto>();
            foreach (var t in link.TeamLinks)
            {
                var dto = new IbbaTeamLinkDto
                {
                    Id = t.Id,
                    TeamName = t.TeamName,
                    TeamUrl = t.TeamUrl,
                    TeamLogoUrl = t.TeamLogoUrl,
                    LinkedTeamId = t.LinkedTeamId,
                    LinkedTeamName = t.LinkedTeam?.Name,
                    IbbaLeagueUrl = t.IbbaLeagueUrl,
                    IbbaLeagueName = t.IbbaLeagueName,
                };

                if (!string.IsNullOrEmpty(t.IbbaLeagueUrl))
                {
                    var standings = await _context.IbbaStandings
                        .Where(s => s.IbbaLeagueUrl == t.IbbaLeagueUrl)
                        .OrderBy(s => s.Position)
                        .ToListAsync();
                    dto.TotalTeams = standings.Count;
                    var own = standings.FirstOrDefault(s => s.TeamName.Contains(t.TeamName) || t.TeamName.Contains(s.TeamName));
                    dto.Position = own?.Position;
                }

                teamDtos.Add(dto);
            }

            return new IbbaLinkStatusDto
            {
                PlayerId = link.PlayerId,
                IbbaPlayerUrl = link.IbbaPlayerUrl,
                LastSyncedAt = link.LastSyncedAt,
                LastSyncError = link.LastSyncError,
                Teams = teamDtos
            };
        }

        public async Task<IbbaLinkStatusDto?> LinkTeamAsync(int ibbaTeamLinkId, int teamId, int requestingUserId)
        {
            var teamLink = await _context.IbbaTeamLinks
                .Include(t => t.PlayerIbbaLink)
                .FirstOrDefaultAsync(t => t.Id == ibbaTeamLinkId);
            if (teamLink == null) return null;

            if (!await _playerService.CanAccessPlayerAsync(teamLink.PlayerIbbaLink.PlayerId, requestingUserId)) return null;
            if (!await OwnsTeamAsync(teamId, requestingUserId)) return null;

            teamLink.LinkedTeamId = teamId;
            await _context.SaveChangesAsync();

            // Now that this team has somewhere to put games, sync it immediately
            // rather than waiting for the next scheduled/manual sync.
            await RunSyncAsync(teamLink.PlayerIbbaLink);

            return await GetLinkStatusAsync(teamLink.PlayerIbbaLink.PlayerId, requestingUserId);
        }

        public async Task<List<IbbaStandingDto>> GetStandingsAsync(string leagueUrl)
        {
            var rows = await _context.IbbaStandings
                .Where(s => s.IbbaLeagueUrl == leagueUrl)
                .OrderBy(s => s.Position)
                .ToListAsync();

            return rows.Select(s => new IbbaStandingDto
            {
                Position = s.Position,
                TeamName = s.TeamName,
                TeamUrl = s.TeamUrl,
                GamesPlayed = s.GamesPlayed,
                Wins = s.Wins,
                Losses = s.Losses,
                Technical = s.Technical,
                PointsFor = s.PointsFor,
                PointsAgainst = s.PointsAgainst,
                Diff = s.Diff,
                LeaguePoints = s.LeaguePoints
            }).ToList();
        }

        // ---- Sync orchestration ----

        private async Task RunSyncAsync(PlayerIbbaLink link)
        {
            try
            {
                var builder = new IbbaReportBuilder(CreateIbbaHttpClient());
                var (player, teamReports) = await builder.BuildAsync(link.IbbaPlayerUrl);

                foreach (var report in teamReports)
                {
                    var teamLink = await _context.IbbaTeamLinks.FirstOrDefaultAsync(t =>
                        t.PlayerIbbaLinkId == link.Id && t.IbbaTeamSlugId == report.Team.TeamSlugId);

                    if (teamLink == null)
                    {
                        teamLink = new IbbaTeamLink
                        {
                            PlayerIbbaLinkId = link.Id,
                            IbbaTeamSlugId = report.Team.TeamSlugId,
                        };
                        _context.IbbaTeamLinks.Add(teamLink);
                    }

                    teamLink.IbbaTeamExportId = report.Team.TeamSlugId;
                    teamLink.TeamName = report.Team.TeamName;
                    teamLink.TeamUrl = report.Team.TeamUrl;
                    teamLink.TeamLogoUrl = string.IsNullOrEmpty(report.Team.TeamLogoUrl) ? teamLink.TeamLogoUrl : report.Team.TeamLogoUrl;
                    teamLink.IbbaLeagueUrl = string.IsNullOrEmpty(report.Team.LeagueUrl) ? teamLink.IbbaLeagueUrl : report.Team.LeagueUrl;
                    teamLink.IbbaLeagueName = string.IsNullOrEmpty(report.Team.LeagueName) ? teamLink.IbbaLeagueName : report.Team.LeagueName;
                    await _context.SaveChangesAsync(); // ensure teamLink.Id exists before games/standings reference it

                    if (teamLink.LinkedTeamId.HasValue)
                    {
                        await UpsertGamesAsync(teamLink, report.Games);
                    }

                    if (!string.IsNullOrEmpty(teamLink.IbbaLeagueUrl))
                    {
                        await ReplaceStandingsAsync(teamLink.IbbaLeagueUrl!, teamLink.IbbaLeagueName ?? "", report.Standings);
                    }
                }

                link.LastSyncedAt = DateTime.UtcNow;
                link.LastSyncError = null;
            }
            catch (Exception ex)
            {
                link.LastSyncError = ex.Message;
            }

            await _context.SaveChangesAsync();
        }

        private async Task UpsertGamesAsync(IbbaTeamLink teamLink, List<IbbaGameRow> rows)
        {
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.Code)) continue;

                var isHome = row.HomeTeamCode == teamLink.IbbaTeamSlugId;
                var opponentName = isHome ? row.AwayTeam : row.HomeTeam;
                var teamScore = isHome ? row.HomeScore : row.AwayScore;
                var opponentScore = isHome ? row.AwayScore : row.HomeScore;
                var gameDate = ParseGameDate(row.Date, row.Time);

                var existing = await _context.Games.FirstOrDefaultAsync(g => g.IbbaGameCode == row.Code);
                if (existing == null)
                {
                    _context.Games.Add(new Game
                    {
                        TeamId = teamLink.LinkedTeamId!.Value,
                        IbbaGameCode = row.Code,
                        IbbaTeamLinkId = teamLink.Id,
                        IsHomeGame = isHome,
                        GameType = row.IsCup ? "Cup" : "League",
                        OpponentName = opponentName,
                        GameDate = gameDate,
                        Location = row.Venue,
                        Status = teamScore.HasValue ? "Completed" : "Upcoming",
                        TeamScore = teamScore,
                        OpponentScore = opponentScore,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    });
                }
                else
                {
                    // Non-destructive refresh: a resync only ever fills in fields the
                    // user hasn't already resolved themselves (via live tracking or a
                    // manual edit) - it never overwrites a game once it has a score.
                    if (string.IsNullOrEmpty(existing.Location) && !string.IsNullOrEmpty(row.Venue))
                        existing.Location = row.Venue;

                    if (existing.Status == "Upcoming" && teamScore.HasValue)
                    {
                        existing.TeamScore = teamScore;
                        existing.OpponentScore = opponentScore;
                        existing.Status = "Completed";
                    }

                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();
        }

        private async Task ReplaceStandingsAsync(string leagueUrl, string leagueName, List<IbbaStandingRow> rows)
        {
            var existing = _context.IbbaStandings.Where(s => s.IbbaLeagueUrl == leagueUrl);
            _context.IbbaStandings.RemoveRange(existing);

            foreach (var row in rows)
            {
                _context.IbbaStandings.Add(new IbbaStanding
                {
                    IbbaLeagueUrl = leagueUrl,
                    IbbaLeagueName = leagueName,
                    Position = row.Position,
                    TeamName = row.TeamName,
                    TeamUrl = row.TeamUrl,
                    GamesPlayed = row.GamesPlayed,
                    Wins = row.Wins,
                    Losses = row.Losses,
                    Technical = row.Technical,
                    PointsFor = row.PointsFor,
                    PointsAgainst = row.PointsAgainst,
                    Diff = row.Diff,
                    LeaguePoints = row.LeaguePoints,
                    SyncedAt = DateTime.UtcNow,
                });
            }

            await _context.SaveChangesAsync();
        }

        private static DateTime ParseGameDate(string date, string time)
        {
            var combined = string.IsNullOrWhiteSpace(time) ? date : $"{date} {time}";
            var formats = new[] { "dd-MM-yyyy HH:mm", "dd-MM-yyyy" };
            if (DateTime.TryParseExact(combined, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return DateTime.UtcNow;
        }
    }
}
