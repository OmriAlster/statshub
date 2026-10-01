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
        Task<CreatePlayerFromIbbaResultDto> CreatePlayerFromIbbaAsync(string ibbaPlayerUrl, Guid requestingUserId);
        Task<IbbaLinkStatusDto?> LinkPlayerAsync(Guid playerId, string ibbaPlayerUrl, Guid requestingUserId);
        Task<bool> UnlinkPlayerAsync(Guid playerId, Guid requestingUserId);
        Task<IbbaLinkStatusDto?> SyncPlayerAsync(Guid playerId, Guid requestingUserId);
        // For the nightly background sync only - no signed-in user, so no
        // access check. Never expose this through a controller.
        Task SyncLinkForScheduledJobAsync(Guid linkId);
        Task<IbbaLinkStatusDto?> GetLinkStatusAsync(Guid playerId, Guid requestingUserId);
        Task<IbbaLinkStatusDto?> LinkTeamAsync(Guid ibbaTeamId, Guid teamId, Guid requestingUserId, Guid? playerId = null);
        Task<IbbaLinkStatusDto?> CreateTeamForIbbaTeamAsync(Guid ibbaTeamId, Guid playerId, Guid requestingUserId);
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
        private readonly ISeasonService _seasonService;
        private readonly RequestTimings _timings;
        private readonly IbbaSyncTracker _syncTracker;

        public IbbaService(AppDbContext context, IPlayerService playerService, IHttpClientFactory httpClientFactory, IPushNotificationService push, ILogger<IbbaService> logger, IServiceScopeFactory scopeFactory, ISeasonService seasonService, RequestTimings timings, IbbaSyncTracker syncTracker)
        {
            _timings = timings;
            _syncTracker = syncTracker;
            _seasonService = seasonService;
            _context = context;
            _playerService = playerService;
            _httpClientFactory = httpClientFactory;
            _push = push;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private HttpClient CreateIbbaHttpClient() => _httpClientFactory.CreateClient("Ibba");

        private async Task<bool> OwnsTeamAsync(Guid teamId, Guid requestingUserId) =>
            await _context.Teams.AnyAsync(t =>
                t.Id == teamId && (
                    t.Season.UserId == requestingUserId ||
                    t.PlayerTeams.Any(pt => pt.Player.Parents.Any(pp => pp.UserId == requestingUserId))
                ));

        // The existing teams (not linked to IBBA) the pop-up offers to add the
        // player's IBBA teams to - one IBBA team at a time, for as long as any
        // is left:
        //  - the player's own teams without IBBA;
        //  - for a player with no teams at all, the family's teams without IBBA
        //    (made by the user, or with one of their children on it).
        // Empty -> nothing to ask; IBBA teams get their own new team.
        private async Task<List<Team>> ExistingTeamsToOfferAsync(Guid playerId)
        {
            var own = await _context.Teams
                .Where(t => t.PlayerTeams.Any(pt => pt.PlayerId == playerId))
                .ToListAsync();
            if (own.Count > 0) return own.Where(t => t.IbbaTeamId == null).OrderBy(t => t.Name).ToList();

            var familyUserId = await _context.Players.Where(p => p.Id == playerId).Select(p => p.UserId).FirstAsync();
            return await _context.Teams
                .Where(t => t.IbbaTeamId == null && (
                    t.Season.UserId == familyUserId ||
                    t.PlayerTeams.Any(pt => pt.Player.Parents.Any(pp => pp.UserId == familyUserId))))
                .OrderBy(t => t.Name)
                .ToListAsync();
        }

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

        public async Task<IbbaLinkStatusDto?> LinkPlayerAsync(Guid playerId, string ibbaPlayerUrl, Guid requestingUserId)
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

            await SyncTeamsNowGamesInBackgroundAsync(link);
            return await _timings.MeasureAsync("db-link-status", () => GetLinkStatusAsync(playerId, requestingUserId));
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
        public async Task<CreatePlayerFromIbbaResultDto> CreatePlayerFromIbbaAsync(string ibbaPlayerUrl, Guid requestingUserId)
        {
            var scraper = new IbbaPlayerScraper(CreateIbbaHttpClient());
            var info = await scraper.GetPlayerInfoAsync(ibbaPlayerUrl); // timed as ibba-player-page

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

            await SyncTeamsNowGamesInBackgroundAsync(link, info);

            // The sync just set the profile picture from the IBBA photo (on
            // a separately-fetched, separately-tracked Player entity) - the
            // "player" object above was captured before that write, so
            // returning it as-is would hand back a stale DTO with no photo,
            // and the frontend would show a blank avatar until a full reload.
            var refreshedPlayer = await _playerService.GetPlayerByIdAsync(player.Id, requestingUserId) ?? player;
            var ibba = await _timings.MeasureAsync("db-link-status", () => GetLinkStatusAsync(player.Id, requestingUserId));

            return new CreatePlayerFromIbbaResultDto { Player = refreshedPlayer, Ibba = ibba };
        }

        public async Task<bool> UnlinkPlayerAsync(Guid playerId, Guid requestingUserId)
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

        public async Task<IbbaLinkStatusDto?> SyncPlayerAsync(Guid playerId, Guid requestingUserId)
        {
            if (!await _playerService.CanAccessPlayerAsync(playerId, requestingUserId)) return null;

            var link = await _context.PlayerIbbaLinks.FirstOrDefaultAsync(l => l.PlayerId == playerId);
            if (link == null) return null;

            await SyncTeamsNowGamesInBackgroundAsync(link);
            return await _timings.MeasureAsync("db-link-status", () => GetLinkStatusAsync(playerId, requestingUserId));
        }

        public async Task SyncLinkForScheduledJobAsync(Guid linkId)
        {
            var link = await _context.PlayerIbbaLinks.FirstOrDefaultAsync(l => l.Id == linkId);
            if (link == null) return; // unlinked since the job listed it

            // Never throws for a scrape/parse failure - it's recorded on the
            // link as LastSyncError, same as a manual sync.
            await RunSyncAsync(link);
        }

        public async Task<IbbaLinkStatusDto?> GetLinkStatusAsync(Guid playerId, Guid requestingUserId)
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

            var existingTeams = teamDtos.Any(t => t.LinkedTeamId == null)
                ? (await ExistingTeamsToOfferAsync(playerId)).Select(t => new TeamDto { Id = t.Id, Name = t.Name }).ToList()
                : new List<TeamDto>();

            return new IbbaLinkStatusDto
            {
                PlayerId = link.PlayerId,
                IbbaPlayerUrl = link.IbbaPlayerUrl,
                LastSyncedAt = link.LastSyncedAt,
                LastSyncError = link.LastSyncError,
                Teams = teamDtos,
                ExistingTeams = existingTeams,
                GamesLoading = _syncTracker.IsRunning(link.Id),
            };
        }

        public async Task<IbbaLinkStatusDto?> LinkTeamAsync(Guid ibbaTeamId, Guid teamId, Guid requestingUserId, Guid? playerId = null)
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
            if (playerId != null) candidateLinks = candidateLinks.Where(l => l.PlayerId == playerId).ToList();

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
            // The existing team may not have this player on it yet (e.g. a
            // team made before the player was added).
            if (!await _context.PlayerTeams.AnyAsync(pt => pt.PlayerId == accessibleLink.PlayerId && pt.TeamId == teamId))
            {
                _context.PlayerTeams.Add(new PlayerTeam { PlayerId = accessibleLink.PlayerId, TeamId = teamId, CreatedAt = DateTime.UtcNow });
            }
            await _context.SaveChangesAsync();

            // No existing team left to offer -> the player's other IBBA teams
            // get their own new team; otherwise the pop-up asks about the next.
            if ((await ExistingTeamsToOfferAsync(accessibleLink.PlayerId)).Count == 0)
            {
                await CreateTeamsForUndecidedIbbaTeamsAsync(accessibleLink);
            }

            // Now that this team has somewhere to put games, sync it immediately
            // rather than waiting for the next scheduled/manual sync.
            StartBackgroundSync(accessibleLink.Id);

            return await _timings.MeasureAsync("db-link-status", () => GetLinkStatusAsync(accessibleLink.PlayerId, requestingUserId));
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

        // The whole sync, start to finish - the nightly job, and a background
        // sync started after a team is linked in the pop-up. Never throws for a
        // scrape/parse failure: it's recorded on the link as LastSyncError.
        private async Task RunSyncAsync(PlayerIbbaLink link, IbbaPlayerInfo? prefetchedPlayer = null)
        {
            using var whole = RequestTimings.Time("sync-total");
            try
            {
                var builder = new IbbaReportBuilder(CreateIbbaHttpClient());
                var teams = await SyncTeamsAsync(link, builder, prefetchedPlayer);
                await SyncGamesAndStandingsAsync(link, builder, teams);
                link.LastSyncedAt = DateTime.UtcNow;
                link.LastSyncError = null;
            }
            catch (Exception ex)
            {
                link.LastSyncError = ex.Message;
            }

            await _context.SaveChangesAsync();
        }

        // A link / create / "Sync" a parent is waiting on: the player's teams
        // (quick - player page plus each team's own page, usually cached by
        // IBBA) now, so the answer shows them right away (or asks about them);
        // the games and standings - the always-fresh games spreadsheet is
        // IBBA's slowest page - load in the background. GetLinkStatusAsync
        // reports GamesLoading meanwhile, and the page polls until done.
        private async Task SyncTeamsNowGamesInBackgroundAsync(PlayerIbbaLink link, IbbaPlayerInfo? prefetchedPlayer = null)
        {
            List<(IbbaTeamPage Page, IbbaTeam Team)> teams;
            using (RequestTimings.Time("sync-teams-now"))
            {
                try
                {
                    teams = await SyncTeamsAsync(link, new IbbaReportBuilder(CreateIbbaHttpClient()), prefetchedPlayer);
                    link.LastSyncError = null;
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    link.LastSyncError = ex.Message;
                    await _context.SaveChangesAsync();
                    return;
                }
            }

            if (!_syncTracker.TryStart(link.Id)) return; // loading already - it runs again once that's done
            var linkId = link.Id;
            var pages = teams.Select(t => t.Page).ToList();
            _ = Task.Run(() => RequestTimings.RunBackgroundAsync("games-and-standings", _logger, async _ =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = (IbbaService)scope.ServiceProvider.GetRequiredService<IIbbaService>();
                    await service.FinishSyncAsync(linkId, pages);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background games/standings load failed for link {LinkId}", linkId);
                }
                finally
                {
                    if (_syncTracker.Finish(linkId)) StartBackgroundSync(linkId); // asked for again meanwhile
                }
            }));
        }

        // The background half of SyncTeamsNowGamesInBackgroundAsync, in its
        // own DI scope (the request's is gone by now).
        private async Task FinishSyncAsync(Guid linkId, List<IbbaTeamPage> pages)
        {
            var link = await _context.PlayerIbbaLinks.FirstOrDefaultAsync(l => l.Id == linkId);
            if (link == null) return; // unlinked meanwhile

            try
            {
                var urls = pages.Select(p => p.Team.TeamUrl).ToList();
                var teamsByUrl = await _context.IbbaTeams.Where(t => urls.Contains(t.TeamUrl)).ToDictionaryAsync(t => t.TeamUrl);
                var teams = pages.Where(p => teamsByUrl.ContainsKey(p.Team.TeamUrl)).Select(p => (p, teamsByUrl[p.Team.TeamUrl])).ToList();
                await SyncGamesAndStandingsAsync(link, new IbbaReportBuilder(CreateIbbaHttpClient()), teams);
                link.LastSyncedAt = DateTime.UtcNow;
                link.LastSyncError = null;
            }
            catch (Exception ex)
            {
                link.LastSyncError = ex.Message;
            }

            await _context.SaveChangesAsync();
        }

        // A whole sync in the background, for a team just linked in the
        // pop-up - the answer comes back at once, games follow.
        private void StartBackgroundSync(Guid linkId)
        {
            if (!_syncTracker.TryStart(linkId)) return;
            _ = Task.Run(() => RequestTimings.RunBackgroundAsync("sync", _logger, async _ =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = (IbbaService)scope.ServiceProvider.GetRequiredService<IIbbaService>();
                    var link = await service._context.PlayerIbbaLinks.FirstOrDefaultAsync(l => l.Id == linkId);
                    if (link != null) await service.RunSyncAsync(link);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background sync failed for link {LinkId}", linkId);
                }
                finally
                {
                    if (_syncTracker.Finish(linkId)) StartBackgroundSync(linkId); // asked for again meanwhile
                }
            }));
        }

        // Stage 1: who the player is and which IBBA teams they're on - each
        // team's identity saved, the player's membership, and an app team for
        // each (or the pop-up's question). Every team handled together: one
        // query for the teams already known, one for the player's membership,
        // one save.
        private async Task<List<(IbbaTeamPage Page, IbbaTeam Team)>> SyncTeamsAsync(PlayerIbbaLink link, IbbaReportBuilder builder, IbbaPlayerInfo? prefetchedPlayer)
        {
            var (player, pages) = await _timings.MeasureAsync("sync-ibba-teams", () => builder.LoadTeamsAsync(link.IbbaPlayerUrl, prefetchedPlayer));

            return await _timings.MeasureAsync("db-teams", async () =>
            {
                if (!string.IsNullOrEmpty(player.PhotoUrl))
                {
                    var playerEntity = await _context.Players.FindAsync(link.PlayerId);
                    if (playerEntity != null) playerEntity.ProfilePictureUrl = player.PhotoUrl;
                }

                var urls = pages.Select(p => p.Team.TeamUrl).ToList();
                var known = await _context.IbbaTeams.Where(t => urls.Contains(t.TeamUrl)).ToDictionaryAsync(t => t.TeamUrl);
                var memberOf = (await _context.PlayerIbbaTeams
                    .Where(pit => pit.PlayerIbbaLinkId == link.Id)
                    .Select(pit => pit.IbbaTeamId)
                    .ToListAsync()).ToHashSet();

                var teams = new List<(IbbaTeamPage Page, IbbaTeam Team)>();
                foreach (var page in pages)
                {
                    var team = ApplyTeamIdentity(known, page.Team);
                    if (memberOf.Add(team.Id))
                        _context.PlayerIbbaTeams.Add(new PlayerIbbaTeam { PlayerIbbaLinkId = link.Id, IbbaTeamId = team.Id });
                    teams.Add((page, team));
                }
                await _context.SaveChangesAsync();

                // Give each IBBA team an app team - automatically, except when
                // the parent has to choose (see AutoLinkAppTeamsAsync).
                await _timings.MeasureAsync("db-autolink", () => AutoLinkAppTeamsAsync(link.PlayerId, teams.Select(t => t.Team).ToList()));
                return teams;
            });
        }

        // Stage 2: each team's games and league standings (IBBA's slow part),
        // then saved - standings first, since resolving a game's opponent
        // needs those IbbaTeam rows to exist.
        private async Task SyncGamesAndStandingsAsync(PlayerIbbaLink link, IbbaReportBuilder builder, List<(IbbaTeamPage Page, IbbaTeam Team)> teams)
        {
            var reports = await _timings.MeasureAsync("sync-ibba-games-standings",
                () => Task.WhenAll(teams.Select(t => builder.LoadGamesAndStandingsAsync(t.Page))));

            await _timings.MeasureAsync("db-standings", async () =>
            {
                for (var i = 0; i < teams.Count; i++)
                {
                    var (report, team) = (reports[i], teams[i].Team);
                    if (!string.IsNullOrEmpty(report.Team.LeagueUrl)) team.LeagueUrl = report.Team.LeagueUrl;
                    if (!string.IsNullOrEmpty(report.Team.LeagueName)) team.LeagueName = report.Team.LeagueName;
                    if (!string.IsNullOrEmpty(team.LeagueUrl) && report.Standings.Count > 0)
                    {
                        var standingsTeamIds = await UpsertStandingsAsync(team.LeagueUrl, team.LeagueName, report.Standings);
                        EnqueueCrestBackfill(standingsTeamIds);
                    }
                }
                await _context.SaveChangesAsync();
            });

            // Leagues are known now - two teams of the player's that share a
            // name get it added.
            await AddLeagueToClashingTeamNamesAsync(link.PlayerId);

            // Games, for every IBBA team the player's app team is linked to.
            var teamIds = teams.Select(t => t.Team.Id).ToList();
            var linked = (await _context.Teams
                .Where(t => t.IbbaTeamId != null && teamIds.Contains(t.IbbaTeamId!.Value) && t.PlayerTeams.Any(pt => pt.PlayerId == link.PlayerId))
                .Select(t => t.IbbaTeamId!.Value)
                .ToListAsync()).ToHashSet();
            for (var i = 0; i < teams.Count; i++)
            {
                if (linked.Contains(teams[i].Team.Id))
                    await _timings.MeasureAsync("db-games", () => UpsertGamesAsync(teams[i].Team, reports[i].Games));
            }
        }

        // Makes sure the player is on an app team for each of their IBBA teams,
        // with no "Create team" button to press:
        //   1. already on a team linked to it -> nothing to do;
        //   2. there's an existing team to offer (ExistingTeamsToOfferAsync) ->
        //      leave it for the parent: the Player Profiles page asks, one IBBA
        //      team at a time, whether to add it to an existing team or create
        //      a new one - right after linking/creating;
        //   3. otherwise -> create a new team (named by NameForNewTeam) and link it.
        // Runs on every sync (manual, nightly, first link), so a new IBBA team
        // mid-season gets its app team the same way.
        internal async Task AutoLinkAppTeamsAsync(Guid playerId, IReadOnlyList<IbbaTeam> ibbaTeams)
        {
            var player = await _context.Players.FindAsync(playerId);
            if (player == null) return;
            // Decided up front for all the IBBA teams at once, so none is
            // created behind the parent's back while there's a choice to make.
            var waitForParent = (await ExistingTeamsToOfferAsync(playerId)).Count > 0;

            var playerTeams = await _context.PlayerTeams
                .Where(pt => pt.PlayerId == playerId)
                .Include(pt => pt.Team)
                .ToListAsync();
            var teamNames = playerTeams.Select(pt => pt.Team.Name).ToList();
            Season? season = null;

            foreach (var ibbaTeam in ibbaTeams)
            {
                if (playerTeams.Any(pt => pt.Team.IbbaTeamId == ibbaTeam.Id)) continue;

                if (waitForParent) continue;

                season ??= await _seasonService.GetOrCreateCurrentSeasonAsync(player.UserId);
                var created = await CreateLinkedTeamAsync(player, ibbaTeam, ibbaTeams.Select(t => t.Name), teamNames, season, save: false);
                teamNames.Add(created.Name);
            }
            if (season != null) await _context.SaveChangesAsync();

            await AddLeagueToClashingTeamNamesAsync(playerId);
        }

        // Teams created before the league-name rule existed (the old "Create
        // team" button) can still share a plain name - e.g. two teams both
        // called "מכבי תל מונד". Any of the player's IBBA-linked teams that
        // still carries exactly its plain IBBA name and clashes with another of
        // the player's teams gets its league added. A name the parent changed
        // themselves is never touched.
        internal async Task AddLeagueToClashingTeamNamesAsync(Guid playerId)
        {
            static string Key(string name) => name.Trim().ToLowerInvariant();

            var teams = await _context.PlayerTeams
                .Where(pt => pt.PlayerId == playerId)
                .Include(pt => pt.Team).ThenInclude(t => t.IbbaTeam)
                .Select(pt => pt.Team)
                .ToListAsync();

            // Decided from the names as they are now, before renaming any - so
            // both teams of a clashing pair get their league, not just one.
            var clashing = teams
                .Where(t => teams.Count(other => Key(other.Name) == Key(t.Name)) > 1)
                .ToList();

            var changed = false;
            foreach (var team in clashing)
            {
                var ibba = team.IbbaTeam;
                if (ibba == null || string.IsNullOrWhiteSpace(ibba.LeagueName)) continue;
                if (Key(team.Name) != Key(ibba.Name)) continue; // renamed by the parent

                team.Name = $"{ibba.Name.Trim()} - {ibba.LeagueName.Trim()}";
                team.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }

            if (changed) await _context.SaveChangesAsync();
        }

        // The parent chose "create a new team" for an IBBA team in the pop-up.
        public async Task<IbbaLinkStatusDto?> CreateTeamForIbbaTeamAsync(Guid ibbaTeamId, Guid playerId, Guid requestingUserId)
        {
            if (!await _playerService.CanAccessPlayerAsync(playerId, requestingUserId)) return null;

            var link = await _context.PlayerIbbaLinks
                .Include(l => l.Teams).ThenInclude(pit => pit.IbbaTeam)
                .FirstOrDefaultAsync(l => l.PlayerId == playerId);
            var ibbaTeam = link?.Teams.FirstOrDefault(pit => pit.IbbaTeamId == ibbaTeamId)?.IbbaTeam;
            var player = await _context.Players.FindAsync(playerId);
            if (link == null || ibbaTeam == null || player == null) return null;

            var playerTeams = await _context.PlayerTeams.Where(pt => pt.PlayerId == playerId).Include(pt => pt.Team).ToListAsync();
            if (!playerTeams.Any(pt => pt.Team.IbbaTeamId == ibbaTeamId))
            {
                await CreateLinkedTeamAsync(player, ibbaTeam, link.Teams.Select(pit => pit.IbbaTeam.Name), playerTeams.Select(pt => pt.Team.Name));
                StartBackgroundSync(link.Id); // the new team's schedule
            }

            return await _timings.MeasureAsync("db-link-status", () => GetLinkStatusAsync(playerId, requestingUserId));
        }

        private async Task CreateTeamsForUndecidedIbbaTeamsAsync(PlayerIbbaLink link)
        {
            var player = await _context.Players.FindAsync(link.PlayerId);
            if (player == null) return;
            var ibbaTeams = await _context.PlayerIbbaTeams
                .Where(pit => pit.PlayerIbbaLinkId == link.Id)
                .Select(pit => pit.IbbaTeam)
                .ToListAsync();

            foreach (var ibbaTeam in ibbaTeams)
            {
                var playerTeams = await _context.PlayerTeams.Where(pt => pt.PlayerId == player.Id).Include(pt => pt.Team).ToListAsync();
                if (playerTeams.Any(pt => pt.Team.IbbaTeamId == ibbaTeam.Id)) continue;
                await CreateLinkedTeamAsync(player, ibbaTeam, ibbaTeams.Select(t => t.Name), playerTeams.Select(pt => pt.Team.Name));
            }
        }

        // The team and the player's place on it in one save (UUIDs exist before
        // saving) - or none, when the caller saves several at once.
        private async Task<Team> CreateLinkedTeamAsync(Player player, IbbaTeam ibbaTeam, IEnumerable<string> playerIbbaTeamNames, IEnumerable<string> playerTeamNames, Season? season = null, bool save = true)
        {
            season ??= await _seasonService.GetOrCreateCurrentSeasonAsync(player.UserId);
            var team = new Team
            {
                SeasonId = season.Id,
                Name = NameForNewTeam(ibbaTeam, playerIbbaTeamNames, playerTeamNames),
                IbbaTeamId = ibbaTeam.Id,
                CreatedAt = DateTime.UtcNow,
            };
            _context.Teams.Add(team);
            _context.PlayerTeams.Add(new PlayerTeam { PlayerId = player.Id, TeamId = team.Id, CreatedAt = DateTime.UtcNow });
            if (save) await _context.SaveChangesAsync();
            return team;
        }

        // A player on two IBBA teams sharing a name (the same club in two
        // leagues/age groups, e.g. both "מכבי תל מונד") would otherwise get two
        // identically named teams everywhere in the app. When the name isn't
        // unique for this player - among their IBBA teams or teams they're
        // already on - the league is added: "מכבי תל מונד - נוער ארצית שרון".
        internal static string NameForNewTeam(IbbaTeam ibbaTeam, IEnumerable<string> playerIbbaTeamNames, IEnumerable<string> playerTeamNames)
        {
            static string Key(string name) => name.Trim().ToLowerInvariant();
            var name = ibbaTeam.Name.Trim();
            var ambiguous = playerIbbaTeamNames.Count(n => Key(n) == Key(name)) > 1
                || playerTeamNames.Any(n => Key(n) == Key(name));
            return ambiguous && !string.IsNullOrWhiteSpace(ibbaTeam.LeagueName) ? $"{name} - {ibbaTeam.LeagueName.Trim()}" : name;
        }

        // Find-or-create this one team by its URL (the stable identifier), and
        // refresh its identity fields. Logo is only ever set here if it wasn't
        // already known - the standings scrape often finds a team's own logo for
        // free (its own widget on its own page), which saves a separate fetch.
        // An IBBA team's row from what its own page says, found among the
        // already-loaded known teams or added (its UUID exists straight away,
        // so nothing needs saving first).
        private IbbaTeam ApplyTeamIdentity(Dictionary<string, IbbaTeam> known, IbbaPlayerTeamInfo team)
        {
            if (!known.TryGetValue(team.TeamUrl, out var existing))
            {
                existing = new IbbaTeam { TeamUrl = team.TeamUrl };
                _context.IbbaTeams.Add(existing);
                known[team.TeamUrl] = existing;
            }

            existing.IbbaTeamId = team.TeamSlugId;
            existing.Name = team.TeamName;
            if (string.IsNullOrEmpty(existing.LogoUrl) && !string.IsNullOrEmpty(team.TeamLogoUrl))
                existing.LogoUrl = team.TeamLogoUrl;
            if (!string.IsNullOrEmpty(team.LeagueUrl)) existing.LeagueUrl = team.LeagueUrl;
            if (!string.IsNullOrEmpty(team.LeagueName)) existing.LeagueName = team.LeagueName;
            return existing;
        }

        // Every team in the league's standings gets upserted here, not just the
        // one the player is on - this is what lets an opponent be resolved by id
        // and get a logo, the same as your own team. Returns every touched
        // team's id, so the caller can queue a crest backfill for them.
        private async Task<List<Guid>> UpsertStandingsAsync(string leagueUrl, string leagueName, List<IbbaStandingRow> rows)
        {
            var urls = rows.Where(r => !string.IsNullOrEmpty(r.TeamUrl)).Select(r => r.TeamUrl).Distinct().ToList();
            if (urls.Count == 0) return new List<Guid>();

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
        private void EnqueueCrestBackfill(List<Guid> ibbaTeamIds)
        {
            if (ibbaTeamIds.Count == 0) return;

            _ = Task.Run(() => RequestTimings.RunBackgroundAsync($"crest-backfill ({ibbaTeamIds.Count} teams)", _logger, async timings =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var missing = await context.IbbaTeams
                        .Where(t => ibbaTeamIds.Contains(t.Id) && t.LogoUrl == null)
                        .ToListAsync();
                    timings.Add($"teams-without-crest-{missing.Count}", 0);
                    if (missing.Count == 0) return;

                    var scraper = new IbbaTeamScraper(CreateIbbaHttpClient());
                    using var throttle = new SemaphoreSlim(5);
                    var results = new System.Collections.Concurrent.ConcurrentBag<(Guid Id, string? LogoUrl)>();

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
            }));
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

            _ = Task.Run(() => RequestTimings.RunBackgroundAsync($"opponent-backfill ({unresolvedCodes.Count} teams)", _logger, async timings =>
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
            }));
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
            var teamsByCode = await _timings.MeasureAsync("db-games-find-teams", () => _context.IbbaTeams
                .Where(t => allCodes.Contains(t.IbbaTeamId))
                .ToDictionaryAsync(t => t.IbbaTeamId));

            // Any team not already known by id (typically a cup/friendly game
            // against a team from a different league/age group, which our own
            // league's standings scrape above never sees) gets resolved from
            // its own page in the background instead (see
            // EnqueueOpponentBackfill below) - these Home/AwayTeamId just stay
            // null for now and get patched in once that finishes. Nothing
            // about the game itself is missing in the meantime: the opponent's
            // plain name is already known straight from the Excel export.
            var unresolvedCodes = allCodes.Where(c => !teamsByCode.ContainsKey(c)).ToList();

            // Every game of this batch that's already stored, in one query
            // (not one per row - each is a round trip to the database).
            var rowCodes = rows.Select(r => r.Code).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            var gamesByCode = await _timings.MeasureAsync("db-games-find-existing", () => _context.Games
                .Where(g => g.IbbaGameCode != null && rowCodes.Contains(g.IbbaGameCode))
                .ToDictionaryAsync(g => g.IbbaGameCode!));

            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.Code)) continue;

                var homeTeamId = !string.IsNullOrEmpty(row.HomeTeamCode) && teamsByCode.TryGetValue(row.HomeTeamCode, out var home) ? home.Id : (Guid?)null;
                var awayTeamId = !string.IsNullOrEmpty(row.AwayTeamCode) && teamsByCode.TryGetValue(row.AwayTeamCode, out var away) ? away.Id : (Guid?)null;
                var isHome = row.HomeTeamCode == ownTeam.IbbaTeamId;
                var opponentName = isHome ? row.AwayTeam : row.HomeTeam;
                var gameDate = ParseGameDate(row.Date, row.Time);

                gamesByCode.TryGetValue(row.Code, out var existing);
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
                    gamesByCode[row.Code] = game; // a repeated row updates it, never adds a second

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
                await _timings.MeasureAsync("db-games-save", () => _context.SaveChangesAsync());
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
            Guid ownIbbaTeamId,
            List<(Game Game, string OpponentName)> newlyUpcoming,
            List<(Game Game, string OpponentName)> newlyCompleted,
            List<(Game Game, string OpponentName)> rescheduled)
        {
            if (newlyUpcoming.Count == 0 && newlyCompleted.Count == 0 && rescheduled.Count == 0) return;

            var job = $"game-notifications ({newlyUpcoming.Count} new, {newlyCompleted.Count} final, {rescheduled.Count} rescheduled)";
            _ = Task.Run(() => RequestTimings.RunBackgroundAsync(job, _logger, async timings =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var push = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
                    using var sending = RequestTimings.Time("push-send-all");

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
            }));
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
