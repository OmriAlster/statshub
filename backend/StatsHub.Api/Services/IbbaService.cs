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
        Task<CreatePlayerFromIbbaResultDto> CreatePlayerFromIbbaAsync(string ibbaPlayerUrl, int requestingUserId);
        Task<IbbaLinkStatusDto?> LinkPlayerAsync(int playerId, string ibbaPlayerUrl, int requestingUserId);
        Task<bool> UnlinkPlayerAsync(int playerId, int requestingUserId);
        Task<IbbaLinkStatusDto?> SyncPlayerAsync(int playerId, int requestingUserId);
        // For the nightly background sync only - no signed-in user, so no
        // access check. Never expose this through a controller.
        Task SyncLinkForScheduledJobAsync(int linkId);
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
        private readonly IServiceScopeFactory _scopeFactory;

        public IbbaService(AppDbContext context, IPlayerService playerService, IHttpClientFactory httpClientFactory, IPushNotificationService push, ILogger<IbbaService> logger, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _playerService = playerService;
            _httpClientFactory = httpClientFactory;
            _push = push;
            _logger = logger;
            _scopeFactory = scopeFactory;
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

        // Creates a brand-new StatsHub player straight from an IBBA profile URL
        // and links/syncs it in one call. Fetches the player's own IBBA page
        // exactly once - previously the controller called PreviewAsync (to
        // validate the URL and get a name/DOB to create the player with)
        // and then LinkPlayerAsync re-fetched that same page again a moment
        // later inside RunSyncAsync, a second full page load for data already
        // in hand. That, plus IbbaReportBuilder re-fetching each team's own
        // page four separate times (see IbbaTeamScraper), was most of the real
        // cost behind "creating a player from IBBA" feeling slow.
        public async Task<CreatePlayerFromIbbaResultDto> CreatePlayerFromIbbaAsync(string ibbaPlayerUrl, int requestingUserId)
        {
            var scraper = new IbbaPlayerScraper(CreateIbbaHttpClient());
            var info = await scraper.GetPlayerInfoAsync(ibbaPlayerUrl);

            if (info.Teams.Count == 0)
                throw new InvalidOperationException("Could not find any current team for this player on the IBBA page. Check the URL is a player profile page.");
            if (info.DateOfBirth == null)
                throw new InvalidOperationException("This IBBA profile doesn't list a birth date - add this player manually instead.");

            var nameParts = info.PlayerName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var firstName = nameParts.Length > 1 ? string.Join(' ', nameParts[..^1]) : info.PlayerName;
            var lastName = nameParts.Length > 1 ? nameParts[^1] : "";

            var player = await _playerService.CreatePlayerAsync(requestingUserId, new CreatePlayerDto
            {
                FirstName = firstName,
                LastName = lastName,
                Position = "",
                DateOfBirth = info.DateOfBirth.Value,
            });

            var link = new PlayerIbbaLink { PlayerId = player.Id, IbbaPlayerUrl = ibbaPlayerUrl, CreatedAt = DateTime.UtcNow };
            _context.PlayerIbbaLinks.Add(link);
            await _context.SaveChangesAsync();

            await RunSyncAsync(link, info);

            // RunSyncAsync just set the profile picture from the IBBA photo (on
            // a separately-fetched, separately-tracked Player entity) - the
            // "player" object above was captured before that write, so
            // returning it as-is would hand back a stale DTO with no photo,
            // and the frontend would show a blank avatar until a full reload.
            var refreshedPlayer = await _playerService.GetPlayerByIdAsync(player.Id, requestingUserId) ?? player;
            var ibba = await GetLinkStatusAsync(player.Id, requestingUserId);

            return new CreatePlayerFromIbbaResultDto { Player = refreshedPlayer, Ibba = ibba };
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

        public async Task SyncLinkForScheduledJobAsync(int linkId)
        {
            var link = await _context.PlayerIbbaLinks.FirstOrDefaultAsync(l => l.Id == linkId);
            if (link == null) return; // unlinked since the job listed it

            // Never throws for a scrape/parse failure - it's recorded on the
            // link as LastSyncError, same as a manual sync.
            await RunSyncAsync(link);
        }

        public async Task<IbbaLinkStatusDto?> GetLinkStatusAsync(int playerId, int requestingUserId)
        {
            if (!await _playerService.CanAccessPlayerAsync(playerId, requestingUserId)) return null;

            var link = await _context.PlayerIbbaLinks
                .Include(l => l.Teams)
                .ThenInclude(pit => pit.IbbaTeam)
                .FirstOrDefaultAsync(l => l.PlayerId == playerId);
            if (link == null) return null;

            // The same IbbaTeam can be linked from multiple players' own app
            // Teams (one per parent tracking the same real-world team), so
            // "which app Team is THIS player's" is scoped by playerId, not by
            // the IbbaTeam alone.
            var ibbaTeamIds = link.Teams.Select(pit => pit.IbbaTeamId).ToList();
            var linkedAppTeams = await _context.Teams
                .Where(t => t.IbbaTeamId != null && ibbaTeamIds.Contains(t.IbbaTeamId!.Value) && t.PlayerTeams.Any(pt => pt.PlayerId == playerId))
                .ToListAsync();

            var teamDtos = link.Teams.Select(pit =>
            {
                var appTeam = linkedAppTeams.FirstOrDefault(t => t.IbbaTeamId == pit.IbbaTeamId);
                return new IbbaTeamLinkDto
                {
                    Id = pit.IbbaTeam.Id,
                    TeamName = pit.IbbaTeam.Name,
                    TeamUrl = pit.IbbaTeam.TeamUrl,
                    TeamLogoUrl = pit.IbbaTeam.LogoUrl,
                    LinkedTeamId = appTeam?.Id,
                    LinkedTeamName = appTeam?.Name,
                    IbbaLeagueUrl = pit.IbbaTeam.LeagueUrl,
                    IbbaLeagueName = pit.IbbaTeam.LeagueName,
                    Position = pit.IbbaTeam.LeaguePosition,
                    TotalTeams = pit.IbbaTeam.LeagueTotalTeams,
                };
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

            // The link lives on the app Team, not the IbbaTeam - so two
            // different players' own app Teams can each link to this same
            // shared IbbaTeam without one overwriting the other.
            var team = await _context.Teams.FindAsync(teamId);
            if (team == null) return null;
            team.IbbaTeamId = ibbaTeamId;
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

        private async Task RunSyncAsync(PlayerIbbaLink link, IbbaPlayerInfo? prefetchedPlayer = null)
        {
            try
            {
                var builder = new IbbaReportBuilder(CreateIbbaHttpClient());
                var (player, teamReports) = await builder.BuildAsync(link.IbbaPlayerUrl, prefetchedPlayer);

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
                        var standingsTeamIds = await UpsertStandingsAsync(ownTeam.LeagueUrl, ownTeam.LeagueName, report.Standings);
                        EnqueueCrestBackfill(standingsTeamIds);
                    }

                    // Games only get synced once this player's own app Team is
                    // actually linked - there's nowhere meaningful to attribute
                    // them to otherwise. (The row itself no longer needs to know
                    // which app Team triggered this - see UpsertGamesAsync.)
                    var hasLinkedAppTeam = await _context.Teams
                        .AnyAsync(t => t.IbbaTeamId == ownTeam.Id && t.PlayerTeams.Any(pt => pt.PlayerId == link.PlayerId));
                    if (hasLinkedAppTeam)
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
        // and get a logo, the same as your own team. Returns every touched
        // team's id, so the caller can queue a crest backfill for them.
        private async Task<List<int>> UpsertStandingsAsync(string leagueUrl, string leagueName, List<IbbaStandingRow> rows)
        {
            var urls = rows.Where(r => !string.IsNullOrEmpty(r.TeamUrl)).Select(r => r.TeamUrl).Distinct().ToList();
            if (urls.Count == 0) return new List<int>();

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
                // (EnqueueCrestBackfill is the only thing that sets it, and does
                // so in the background - see below).
            }

            await _context.SaveChangesAsync();
            return existingTeams.Values.Select(t => t.Id).ToList();
        }

        // Fetches crests in the background instead of blocking the sync/player
        // creation on it - a full league is 12-16 teams, and visiting every one
        // of their pages before responding was making "create player from IBBA"
        // noticeably slower, entirely for a cosmetic enrichment nobody's
        // actually waiting to see. Runs in its own DI scope since the request's
        // own scoped AppDbContext gets disposed once the response is sent; best-
        // effort - on failure, or if the app shuts down mid-fetch, a still-
        // missing logo just gets retried by the next sync that touches this team
        // (LogoUrl == null is the only condition ever checked).
        private void EnqueueCrestBackfill(List<int> ibbaTeamIds)
        {
            if (ibbaTeamIds.Count == 0) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var missing = await context.IbbaTeams
                        .Where(t => ibbaTeamIds.Contains(t.Id) && t.LogoUrl == null)
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
                        missing.First(t => t.Id == id).LogoUrl = logoUrl;
                    }
                    await context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background crest backfill failed for {Count} teams", ibbaTeamIds.Count);
                }
            });
        }

        // Resolves opponent teams that never appeared in our own league's
        // standings scrape - fetched by id straight from each team's own page,
        // so a cup/friendly opponent still gets a real IbbaTeam row instead of
        // staying a bare name with no id. Name and crest come off the same
        // page load. Always called from the background (see
        // EnqueueOpponentBackfill) - takes an explicit context since the
        // request's own scoped one is gone by the time this runs.
        private async Task<List<IbbaTeam>> ResolveUnknownOpponentsAsync(AppDbContext context, IbbaTeamScraper scraper, List<string> codes)
        {
            using var throttle = new SemaphoreSlim(5);
            var results = new System.Collections.Concurrent.ConcurrentBag<(string Code, string Url, string Name, string? LogoUrl)>();

            await Task.WhenAll(codes.Select(async code =>
            {
                await throttle.WaitAsync();
                try
                {
                    var resolved = await scraper.ResolveTeamByIdAsync(code);
                    if (resolved != null) results.Add((code, resolved.Value.CanonicalUrl, resolved.Value.Name, resolved.Value.LogoUrl));
                }
                catch
                {
                    // Network hiccup or unexpected page shape - not fatal, this
                    // opponent just stays unresolved until the next sync retries.
                }
                finally
                {
                    throttle.Release();
                }
            }));

            if (results.IsEmpty) return new List<IbbaTeam>();

            // A concurrent sync (another player, another team) could have already
            // created one of these under the same canonical URL - the unique
            // index on TeamUrl would reject a duplicate insert, so reuse it.
            var urls = results.Select(r => r.Url).Distinct().ToList();
            var existingByUrl = await context.IbbaTeams.Where(t => urls.Contains(t.TeamUrl)).ToDictionaryAsync(t => t.TeamUrl);

            var teams = new List<IbbaTeam>();
            foreach (var (code, url, name, logoUrl) in results)
            {
                if (existingByUrl.TryGetValue(url, out var existing))
                {
                    teams.Add(existing);
                    continue;
                }
                var team = new IbbaTeam { IbbaTeamId = code, TeamUrl = url, Name = name, LogoUrl = string.IsNullOrEmpty(logoUrl) ? null : logoUrl };
                context.IbbaTeams.Add(team);
                existingByUrl[url] = team;
                teams.Add(team);
            }

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Opponent team resolution hit a duplicate-team race");
                // Not fatal - these just won't be linked to their games this
                // round; the next sync will find them via the normal lookup.
                return new List<IbbaTeam>();
            }

            return teams;
        }

        // A cup/friendly opponent from outside our own league's standings has
        // no known IbbaTeam yet, and resolving one means visiting a page nobody
        // asked to wait on - the game itself already has everything it needs to
        // display (the opponent's plain name straight from the Excel export),
        // so this runs after the response, in its own DI scope, and patches in
        // Home/AwayTeamId on whichever of these specific rows are still
        // missing it once resolution finishes.
        private void EnqueueOpponentBackfill(List<string> unresolvedCodes, List<IbbaGameRow> rows)
        {
            if (unresolvedCodes.Count == 0) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var scraper = new IbbaTeamScraper(CreateIbbaHttpClient());

                    var resolved = await ResolveUnknownOpponentsAsync(context, scraper, unresolvedCodes);
                    if (resolved.Count == 0) return;

                    var teamsByCode = resolved.ToDictionary(t => t.IbbaTeamId);

                    foreach (var row in rows)
                    {
                        if (string.IsNullOrEmpty(row.Code)) continue;

                        var game = await context.Games.FirstOrDefaultAsync(g => g.IbbaGameCode == row.Code);
                        if (game == null) continue;

                        if (game.HomeTeamId == null && !string.IsNullOrEmpty(row.HomeTeamCode) && teamsByCode.TryGetValue(row.HomeTeamCode, out var home))
                            game.HomeTeamId = home.Id;
                        if (game.AwayTeamId == null && !string.IsNullOrEmpty(row.AwayTeamCode) && teamsByCode.TryGetValue(row.AwayTeamCode, out var away))
                            game.AwayTeamId = away.Id;
                    }

                    await context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background opponent backfill failed for {Count} teams", unresolvedCodes.Count);
                }
            });
        }

        private async Task UpsertGamesAsync(IbbaTeam ownTeam, List<IbbaGameRow> rows)
        {
            // Collected as (Game, OpponentName) so pushes fire only after
            // SaveChangesAsync assigns real IDs, only once per game regardless
            // of how many rows touched it, and without a second lookup for the
            // push body text - OpponentName is never stored on the row itself
            // (see Game.cs), just known briefly here from the export.
            var newlyUpcoming = new List<(Game Game, string OpponentName)>();
            var newlyCompleted = new List<(Game Game, string OpponentName)>();
            var rescheduled = new List<(Game Game, string OpponentName)>();

            // Resolve every team code seen in this batch to an IbbaTeam, by id,
            // globally - never by name. IbbaTeamId is the site's own team id, so
            // it's a safe join key across leagues too (not scoped to ownTeam's
            // league - a team already known from some other context, e.g. being
            // someone else's own team, should still be reused as-is).
            var allCodes = rows
                .SelectMany(row => new[] { row.HomeTeamCode, row.AwayTeamCode })
                .Where(code => !string.IsNullOrEmpty(code))
                .Distinct()
                .ToList();
            var teamsByCode = await _context.IbbaTeams
                .Where(t => allCodes.Contains(t.IbbaTeamId))
                .ToDictionaryAsync(t => t.IbbaTeamId);

            // Any team not already known by id (typically a cup/friendly game
            // against a team from a different league/age group, which our own
            // league's standings scrape above never sees) gets resolved from
            // its own page in the background instead (see
            // EnqueueOpponentBackfill below) - these Home/AwayTeamId just stay
            // null for now and get patched in once that finishes. Nothing
            // about the game itself is missing in the meantime: the opponent's
            // plain name is already known straight from the Excel export.
            var unresolvedCodes = allCodes.Where(c => !teamsByCode.ContainsKey(c)).ToList();

            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.Code)) continue;

                var homeTeamId = !string.IsNullOrEmpty(row.HomeTeamCode) && teamsByCode.TryGetValue(row.HomeTeamCode, out var home) ? home.Id : (int?)null;
                var awayTeamId = !string.IsNullOrEmpty(row.AwayTeamCode) && teamsByCode.TryGetValue(row.AwayTeamCode, out var away) ? away.Id : (int?)null;
                var isHome = row.HomeTeamCode == ownTeam.IbbaTeamId;
                var opponentName = isHome ? row.AwayTeam : row.HomeTeam;
                var gameDate = ParseGameDate(row.Date, row.Time);

                var existing = await _context.Games.FirstOrDefaultAsync(g => g.IbbaGameCode == row.Code);
                if (existing == null)
                {
                    var game = new Game
                    {
                        IbbaGameCode = row.Code,
                        HomeTeamId = homeTeamId,
                        AwayTeamId = awayTeamId,
                        GameType = row.IsCup ? "Cup" : "League",
                        GameDate = gameDate,
                        Location = row.Venue,
                        Status = row.HomeScore.HasValue ? "Completed" : "Upcoming",
                        HomeScore = row.HomeScore,
                        AwayScore = row.AwayScore,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    };
                    _context.Games.Add(game);

                    // Only a brand-new *upcoming* fixture is worth a "new game
                    // added" push - a completed one just showed up via a
                    // historical backfill, nobody needs to be told about it.
                    if (game.Status == "Upcoming") newlyUpcoming.Add((game, opponentName));
                }
                else
                {
                    // Schedule facts (home/away teams, venue, type, date/time) always
                    // track IBBA - the Schedule tab locks editing these fields for an
                    // IBBA-synced game specifically because IBBA is the only source for
                    // them, so there's no risk of clobbering something the user set
                    // themselves. The score/status stays conservative below: once a game
                    // has a real result (live-tracked or already synced), a resync never
                    // overwrites it.
                    existing.HomeTeamId = homeTeamId;
                    existing.AwayTeamId = awayTeamId;
                    existing.Location = row.Venue;
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
                        rescheduled.Add((existing, opponentName));
                    }

                    if (existing.Status == "Upcoming" && row.HomeScore.HasValue)
                    {
                        existing.HomeScore = row.HomeScore;
                        existing.AwayScore = row.AwayScore;
                        existing.Status = "Completed";
                        newlyCompleted.Add((existing, opponentName));
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

            EnqueueOpponentBackfill(unresolvedCodes, rows);
            EnqueueGameNotifications(ownTeam.Id, newlyUpcoming, newlyCompleted, rescheduled);
        }

        // A first-ever sync of a team can easily mean dozens of "new game"
        // pushes at once (every upcoming fixture on the schedule), each of
        // which is its own round trip to the push service per subscriber -
        // real, multi-second latency that nobody is actually waiting to see,
        // since the games themselves are already saved and visible by the
        // time this runs. Dispatched the same way as the other background
        // work here: its own DI scope, best-effort.
        private void EnqueueGameNotifications(
            int ownIbbaTeamId,
            List<(Game Game, string OpponentName)> newlyUpcoming,
            List<(Game Game, string OpponentName)> newlyCompleted,
            List<(Game Game, string OpponentName)> rescheduled)
        {
            if (newlyUpcoming.Count == 0 && newlyCompleted.Count == 0 && rescheduled.Count == 0) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var push = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

                    foreach (var (game, opponentName) in newlyUpcoming)
                    {
                        await push.NotifyIbbaTeamAsync(
                            ownIbbaTeamId,
                            "🏀 New game scheduled",
                            $"vs {opponentName} on {{datetime}}",
                            $"/games/{game.Id}",
                            gameDate: game.GameDate);
                    }

                    foreach (var (game, opponentName) in newlyCompleted)
                    {
                        var isHome = game.HomeTeamId == ownIbbaTeamId;
                        var teamScore = isHome ? game.HomeScore : game.AwayScore;
                        var oppScore = isHome ? game.AwayScore : game.HomeScore;
                        var result = (teamScore ?? 0) > (oppScore ?? 0) ? "W" : "L";
                        await push.NotifyIbbaTeamAsync(
                            ownIbbaTeamId,
                            "Final score",
                            $"{result} {teamScore}-{oppScore} vs {opponentName}",
                            $"/games/{game.Id}");
                    }

                    foreach (var (game, opponentName) in rescheduled)
                    {
                        await push.NotifyIbbaTeamAsync(
                            ownIbbaTeamId,
                            "📅 Game rescheduled",
                            $"vs {opponentName} moved to {{datetime}}",
                            $"/games/{game.Id}",
                            gameDate: game.GameDate);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background game-notification dispatch failed");
                }
            });
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
