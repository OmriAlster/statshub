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
        private readonly IPushNotificationService _push;
        private readonly ILogger<IbbaService> _logger;

        public IbbaService(AppDbContext context, IPlayerService playerService, IHttpClientFactory httpClientFactory, IPushNotificationService push, ILogger<IbbaService> logger)
        {
            _context = context;
            _playerService = playerService;
            _httpClientFactory = httpClientFactory;
            _push = push;
            _logger = logger;
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
                DateOfBirth = info.DateOfBirth,
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
                    // Match by team URL, not name - a substring name match (e.g. "מכבי
                    // בקה" is contained in "מכבי בקה גת") would silently pick the wrong
                    // row whenever one team's name is a prefix of another's.
                    var own = !string.IsNullOrEmpty(t.TeamUrl)
                        ? standings.FirstOrDefault(s => s.TeamUrl == t.TeamUrl)
                        : standings.FirstOrDefault(s => s.TeamName == t.TeamName);
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

            var teamUrls = rows.Select(s => s.TeamUrl).Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList();
            var crests = await _context.IbbaTeamCrests
                .Where(c => teamUrls.Contains(c.TeamUrl))
                .ToDictionaryAsync(c => c.TeamUrl, c => c.LogoUrl);

            return rows.Select(s => new IbbaStandingDto
            {
                Position = s.Position,
                TeamName = s.TeamName,
                TeamUrl = s.TeamUrl,
                LogoUrl = crests.GetValueOrDefault(s.TeamUrl),
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

                if (!string.IsNullOrEmpty(player.PhotoUrl))
                {
                    var playerEntity = await _context.Players.FindAsync(link.PlayerId);
                    if (playerEntity != null) playerEntity.ProfilePictureUrl = player.PhotoUrl;
                }

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
                        await EnsureTeamCrestsAsync(report.Standings);
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
            // Collected so pushes fire only after SaveChangesAsync assigns real
            // IDs, and only once per game regardless of how many rows touched it.
            var newlyUpcoming = new List<Game>();
            var newlyCompleted = new List<Game>();
            var rescheduled = new List<Game>();

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
                    var game = new Game
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
                    };
                    _context.Games.Add(game);

                    // Only a brand-new *upcoming* fixture is worth a "new game
                    // added" push - a completed one just showed up via a
                    // historical backfill, nobody needs to be told about it.
                    if (game.Status == "Upcoming") newlyUpcoming.Add(game);
                }
                else
                {
                    // Schedule facts (opponent, venue, home/away, type, date/time) always
                    // track IBBA - the Schedule tab locks editing these fields for an
                    // IBBA-synced game specifically because IBBA is the only source for
                    // them, so there's no risk of clobbering something the user set
                    // themselves. The score/status stays conservative below: once a game
                    // has a real result (live-tracked or already synced), a resync never
                    // overwrites it.
                    existing.OpponentName = opponentName;
                    existing.Location = row.Venue;
                    existing.IsHomeGame = isHome;
                    existing.GameType = row.IsCup ? "Cup" : "League";

                    // Date/time always tracks IBBA too, regardless of status - no
                    // restrictions here, so a game whose stored time was ever wrong
                    // (e.g. computed before a timezone-conversion fix shipped) gets
                    // corrected by the very next sync instead of staying stuck. A
                    // minute of slack avoids false positives from re-parsing the same
                    // time down to the second.
                    if (Math.Abs((gameDate - existing.GameDate).TotalMinutes) >= 1)
                    {
                        existing.GameDate = gameDate;
                        existing.ReminderSentAt = null;
                        rescheduled.Add(existing);
                    }

                    if (existing.Status == "Upcoming" && teamScore.HasValue)
                    {
                        existing.TeamScore = teamScore;
                        existing.OpponentScore = opponentScore;
                        existing.Status = "Completed";
                        newlyCompleted.Add(existing);
                    }

                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // Another sync (e.g. a different parent following a teammate on the
                // same IBBA team) won the race and committed one of these fixtures
                // first - the unique constraint on IbbaGameCode caught it. Nothing to
                // recover mid-request: whichever game(s) actually got inserted are
                // already correct, and the next sync (this one or theirs) will find
                // them via the normal "existing" lookup instead of trying to
                // duplicate them again. Skip this round's push notifications since
                // it's unclear which rows actually committed.
                _logger.LogWarning(ex, "IBBA game sync hit a duplicate-fixture race for team link {TeamLinkId}", teamLink.Id);
                return;
            }

            foreach (var game in newlyUpcoming)
            {
                await _push.NotifyTeamAsync(
                    game.TeamId,
                    "🏀 New game scheduled",
                    $"vs {game.OpponentName} on {{datetime}}",
                    $"/games/{game.Id}",
                    gameDate: game.GameDate);
            }

            foreach (var game in newlyCompleted)
            {
                var result = (game.TeamScore ?? 0) > (game.OpponentScore ?? 0) ? "W" : "L";
                await _push.NotifyTeamAsync(
                    game.TeamId,
                    "Final score",
                    $"{result} {game.TeamScore}-{game.OpponentScore} vs {game.OpponentName}",
                    $"/games/{game.Id}");
            }

            foreach (var game in rescheduled)
            {
                await _push.NotifyTeamAsync(
                    game.TeamId,
                    "📅 Game rescheduled",
                    $"vs {game.OpponentName} moved to {{datetime}}",
                    $"/games/{game.Id}",
                    gameDate: game.GameDate);
            }
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

        // Every team seen in a standings table gets its crest cached, keyed by team
        // URL - not just the player's own team, so opponents get a logo too. A
        // cached team (found or not - "not every team page renders this widget" is
        // normal) is never re-fetched, so steady-state syncs only ever pay for
        // genuinely new teams. Fetched in parallel (bounded) since a full league is
        // 12-16 teams and this only matters the first time any of them is seen.
        private async Task EnsureTeamCrestsAsync(List<IbbaStandingRow> rows)
        {
            var candidates = rows
                .Where(r => !string.IsNullOrEmpty(r.TeamUrl))
                .GroupBy(r => r.TeamUrl)
                .Select(g => g.First())
                .ToList();
            if (candidates.Count == 0) return;

            var urls = candidates.Select(r => r.TeamUrl).ToList();
            var cachedUrls = await _context.IbbaTeamCrests.Where(c => urls.Contains(c.TeamUrl)).Select(c => c.TeamUrl).ToListAsync();
            var missing = candidates.Where(r => !cachedUrls.Contains(r.TeamUrl)).ToList();
            if (missing.Count == 0) return;

            var scraper = new IbbaTeamScraper(CreateIbbaHttpClient());
            using var throttle = new SemaphoreSlim(5);
            var results = new System.Collections.Concurrent.ConcurrentBag<(string TeamUrl, string? LogoUrl)>();

            await Task.WhenAll(missing.Select(async row =>
            {
                await throttle.WaitAsync();
                try
                {
                    var logo = await scraper.FindTeamLogoUrlAsync(row.TeamUrl, row.TeamName);
                    results.Add((row.TeamUrl, logo));
                }
                catch
                {
                    results.Add((row.TeamUrl, null));
                }
                finally
                {
                    throttle.Release();
                }
            }));

            foreach (var (teamUrl, logoUrl) in results)
            {
                _context.IbbaTeamCrests.Add(new IbbaTeamCrest { TeamUrl = teamUrl, LogoUrl = logoUrl, FetchedAt = DateTime.UtcNow });
            }
            await _context.SaveChangesAsync();
        }

        // IBBA is an Israeli site - every date/time it publishes is Israel wall-clock
        // time, not UTC. Falls back to a fixed UTC+2 (Israel Standard Time, no DST)
        // zone if the OS/container has no "Asia/Jerusalem" tzdata entry, which is
        // close enough to be right most of the year and off by at most an hour
        // during DST rather than by the full un-converted offset.
        private static readonly TimeZoneInfo IsraelTimeZone = ResolveIsraelTimeZone();

        private static TimeZoneInfo ResolveIsraelTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem"); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
            return TimeZoneInfo.CreateCustomTimeZone("Israel-Fallback", TimeSpan.FromHours(2), "Israel (fallback, no DST)", "IST");
        }

        private static DateTime ParseGameDate(string date, string time)
        {
            var combined = string.IsNullOrWhiteSpace(time) ? date : $"{date} {time}";
            var formats = new[] { "dd-MM-yyyy HH:mm", "dd-MM-yyyy" };
            if (DateTime.TryParseExact(combined, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                var israelLocal = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
                return TimeZoneInfo.ConvertTimeToUtc(israelLocal, IsraelTimeZone);
            }
            return DateTime.UtcNow;
        }
    }
}
