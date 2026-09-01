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
        Task<IbbaLinkStatusDto?> LinkTeamAsync(int ibbaTeamId, int teamId, int requestingUserId);
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

            // PlayerIbbaTeams cascade-deletes with the link. The IbbaTeam rows
            // themselves (and any games/opponent-logo links pointing at them) are
            // untouched - they're shared team data, not owned by this one player.
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
                .Include(l => l.Teams)
                .ThenInclude(pit => pit.IbbaTeam)
                .ThenInclude(t => t.LinkedTeam)
                .FirstOrDefaultAsync(l => l.PlayerId == playerId);
            if (link == null) return null;

            var teamDtos = link.Teams.Select(pit => new IbbaTeamLinkDto
            {
                Id = pit.IbbaTeam.Id,
                TeamName = pit.IbbaTeam.Name,
                TeamUrl = pit.IbbaTeam.TeamUrl,
                TeamLogoUrl = pit.IbbaTeam.LogoUrl,
                LinkedTeamId = pit.IbbaTeam.LinkedTeamId,
                LinkedTeamName = pit.IbbaTeam.LinkedTeam?.Name,
                IbbaLeagueUrl = pit.IbbaTeam.LeagueUrl,
                IbbaLeagueName = pit.IbbaTeam.LeagueName,
                Position = pit.IbbaTeam.LeaguePosition,
                TotalTeams = pit.IbbaTeam.LeagueTotalTeams,
            }).ToList();

            return new IbbaLinkStatusDto
            {
                PlayerId = link.PlayerId,
                IbbaPlayerUrl = link.IbbaPlayerUrl,
                LastSyncedAt = link.LastSyncedAt,
                LastSyncError = link.LastSyncError,
                Teams = teamDtos
            };
        }

        public async Task<IbbaLinkStatusDto?> LinkTeamAsync(int ibbaTeamId, int teamId, int requestingUserId)
        {
            var ibbaTeam = await _context.IbbaTeams.FindAsync(ibbaTeamId);
            if (ibbaTeam == null) return null;

            // A team is shared, so more than one of the requesting user's players
            // could be on it - any one of them being accessible is enough.
            var candidateLinks = await _context.PlayerIbbaTeams
                .Include(pit => pit.PlayerIbbaLink)
                .Where(pit => pit.IbbaTeamId == ibbaTeamId)
                .Select(pit => pit.PlayerIbbaLink)
                .ToListAsync();

            PlayerIbbaLink? accessibleLink = null;
            foreach (var candidate in candidateLinks)
            {
                if (await _playerService.CanAccessPlayerAsync(candidate.PlayerId, requestingUserId))
                {
                    accessibleLink = candidate;
                    break;
                }
            }
            if (accessibleLink == null) return null;
            if (!await OwnsTeamAsync(teamId, requestingUserId)) return null;

            ibbaTeam.LinkedTeamId = teamId;
            await _context.SaveChangesAsync();

            // Now that this team has somewhere to put games, sync it immediately
            // rather than waiting for the next scheduled/manual sync.
            await RunSyncAsync(accessibleLink);

            return await GetLinkStatusAsync(accessibleLink.PlayerId, requestingUserId);
        }

        public async Task<List<IbbaStandingDto>> GetStandingsAsync(string leagueUrl)
        {
            var teams = await _context.IbbaTeams
                .Where(t => t.LeagueUrl == leagueUrl)
                .OrderBy(t => t.LeaguePosition)
                .ToListAsync();

            return teams.Select(t => new IbbaStandingDto
            {
                Position = t.LeaguePosition ?? 0,
                TeamName = t.Name,
                TeamUrl = t.TeamUrl,
                LogoUrl = t.LogoUrl,
                GamesPlayed = t.GamesPlayed,
                Wins = t.Wins,
                Losses = t.Losses,
                Technical = t.Technical,
                PointsFor = t.PointsFor,
                PointsAgainst = t.PointsAgainst,
                Diff = t.Diff,
                LeaguePoints = t.LeaguePoints
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
                    var ownTeam = await UpsertTeamIdentityAsync(report.Team);

                    var alreadyOnTeam = await _context.PlayerIbbaTeams
                        .AnyAsync(pit => pit.PlayerIbbaLinkId == link.Id && pit.IbbaTeamId == ownTeam.Id);
                    if (!alreadyOnTeam)
                    {
                        _context.PlayerIbbaTeams.Add(new PlayerIbbaTeam { PlayerIbbaLinkId = link.Id, IbbaTeamId = ownTeam.Id });
                        await _context.SaveChangesAsync();
                    }

                    // Standings cover every team in the league (opponents included),
                    // so this has to run BEFORE games are upserted below - opponent
                    // resolution needs those IbbaTeam rows to already exist.
                    if (!string.IsNullOrEmpty(ownTeam.LeagueUrl) && report.Standings.Count > 0)
                    {
                        await UpsertStandingsAsync(ownTeam.LeagueUrl, ownTeam.LeagueName, report.Standings);
                        await EnsureTeamCrestsAsync(report.Standings);
                    }

                    if (ownTeam.LinkedTeamId.HasValue)
                    {
                        await UpsertGamesAsync(ownTeam, report.Games);
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

        // Find-or-create this one team by its URL (the stable identifier), and
        // refresh its identity fields. Logo is only ever set here if it wasn't
        // already known - the standings scrape often finds a team's own logo for
        // free (its own widget on its own page), which saves a separate fetch.
        private async Task<IbbaTeam> UpsertTeamIdentityAsync(IbbaPlayerTeamInfo team)
        {
            var existing = await _context.IbbaTeams.FirstOrDefaultAsync(t => t.TeamUrl == team.TeamUrl);
            if (existing == null)
            {
                existing = new IbbaTeam { TeamUrl = team.TeamUrl };
                _context.IbbaTeams.Add(existing);
            }

            existing.IbbaTeamId = team.TeamSlugId;
            existing.Name = team.TeamName;
            if (string.IsNullOrEmpty(existing.LogoUrl) && !string.IsNullOrEmpty(team.TeamLogoUrl))
                existing.LogoUrl = team.TeamLogoUrl;
            if (!string.IsNullOrEmpty(team.LeagueUrl)) existing.LeagueUrl = team.LeagueUrl;
            if (!string.IsNullOrEmpty(team.LeagueName)) existing.LeagueName = team.LeagueName;

            await _context.SaveChangesAsync(); // ensure Id exists before anything below references it
            return existing;
        }

        // Every team in the league's standings gets upserted here, not just the
        // one the player is on - this is what lets an opponent be resolved by id
        // and get a logo, the same as your own team.
        private async Task UpsertStandingsAsync(string leagueUrl, string leagueName, List<IbbaStandingRow> rows)
        {
            var urls = rows.Where(r => !string.IsNullOrEmpty(r.TeamUrl)).Select(r => r.TeamUrl).Distinct().ToList();
            if (urls.Count == 0) return;

            var existingTeams = await _context.IbbaTeams
                .Where(t => urls.Contains(t.TeamUrl))
                .ToDictionaryAsync(t => t.TeamUrl);

            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.TeamUrl)) continue;

                if (!existingTeams.TryGetValue(row.TeamUrl, out var team))
                {
                    team = new IbbaTeam { TeamUrl = row.TeamUrl };
                    _context.IbbaTeams.Add(team);
                    existingTeams[row.TeamUrl] = team;
                }

                team.IbbaTeamId = IbbaPlayerScraper.ExtractTeamSlugId(row.TeamUrl);
                team.Name = row.TeamName;
                team.LeagueUrl = leagueUrl;
                team.LeagueName = leagueName;
                team.LeaguePosition = row.Position;
                team.LeagueTotalTeams = rows.Count;
                team.GamesPlayed = row.GamesPlayed;
                team.Wins = row.Wins;
                team.Losses = row.Losses;
                team.Technical = row.Technical;
                team.PointsFor = row.PointsFor;
                team.PointsAgainst = row.PointsAgainst;
                team.Diff = row.Diff;
                team.LeaguePoints = row.LeaguePoints;
                team.SyncedAt = DateTime.UtcNow;
                // LogoUrl is deliberately untouched here - a standings sync never
                // overwrites a crest, whether it's already set or still missing
                // (EnsureTeamCrestsAsync below is the only thing that sets it).
            }

            await _context.SaveChangesAsync();
        }

        // Every team still missing a logo gets one fetched from its own page (the
        // same technique used for the player's own team), in parallel since a
        // full league is 12-16 teams and this only matters the first time any of
        // them is seen - once a team has a logo, it's never re-fetched.
        private async Task EnsureTeamCrestsAsync(List<IbbaStandingRow> rows)
        {
            var urls = rows.Where(r => !string.IsNullOrEmpty(r.TeamUrl)).Select(r => r.TeamUrl).Distinct().ToList();
            if (urls.Count == 0) return;

            var missing = await _context.IbbaTeams
                .Where(t => urls.Contains(t.TeamUrl) && t.LogoUrl == null)
                .ToListAsync();
            if (missing.Count == 0) return;

            var scraper = new IbbaTeamScraper(CreateIbbaHttpClient());
            using var throttle = new SemaphoreSlim(5);
            var results = new System.Collections.Concurrent.ConcurrentBag<(int Id, string? LogoUrl)>();

            await Task.WhenAll(missing.Select(async team =>
            {
                await throttle.WaitAsync();
                try
                {
                    var logo = await scraper.FindTeamLogoUrlAsync(team.TeamUrl, team.Name);
                    results.Add((team.Id, logo));
                }
                catch
                {
                    results.Add((team.Id, null));
                }
                finally
                {
                    throttle.Release();
                }
            }));

            foreach (var (id, logoUrl) in results)
            {
                if (string.IsNullOrEmpty(logoUrl)) continue;
                var team = missing.First(t => t.Id == id);
                team.LogoUrl = logoUrl;
            }
            await _context.SaveChangesAsync();
        }

        private async Task UpsertGamesAsync(IbbaTeam ownTeam, List<IbbaGameRow> rows)
        {
            // Collected so pushes fire only after SaveChangesAsync assigns real
            // IDs, and only once per game regardless of how many rows touched it.
            var newlyUpcoming = new List<Game>();
            var newlyCompleted = new List<Game>();
            var rescheduled = new List<Game>();

            // Resolve every opponent code seen in this batch to an IbbaTeam, by id,
            // within the same league - never by name.
            var opponentCodes = rows
                .Select(row => row.HomeTeamCode == ownTeam.IbbaTeamId ? row.AwayTeamCode : row.HomeTeamCode)
                .Where(code => !string.IsNullOrEmpty(code))
                .Distinct()
                .ToList();
            var opponentsByCode = await _context.IbbaTeams
                .Where(t => t.LeagueUrl == ownTeam.LeagueUrl && opponentCodes.Contains(t.IbbaTeamId))
                .ToDictionaryAsync(t => t.IbbaTeamId);

            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.Code)) continue;

                var isHome = row.HomeTeamCode == ownTeam.IbbaTeamId;
                var opponentName = isHome ? row.AwayTeam : row.HomeTeam;
                var opponentCode = isHome ? row.AwayTeamCode : row.HomeTeamCode;
                var opponentTeamId = !string.IsNullOrEmpty(opponentCode) && opponentsByCode.TryGetValue(opponentCode, out var opp)
                    ? opp.Id
                    : (int?)null;
                var teamScore = isHome ? row.HomeScore : row.AwayScore;
                var opponentScore = isHome ? row.AwayScore : row.HomeScore;
                var gameDate = ParseGameDate(row.Date, row.Time);

                var existing = await _context.Games.FirstOrDefaultAsync(g => g.IbbaGameCode == row.Code);
                if (existing == null)
                {
                    var game = new Game
                    {
                        TeamId = ownTeam.LinkedTeamId!.Value,
                        IbbaGameCode = row.Code,
                        OpponentIbbaTeamId = opponentTeamId,
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
                    existing.OpponentIbbaTeamId = opponentTeamId;
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
                _logger.LogWarning(ex, "IBBA game sync hit a duplicate-fixture race for team {TeamId}", ownTeam.Id);
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
