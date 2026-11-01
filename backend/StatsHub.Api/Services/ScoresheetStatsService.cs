using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StatsHub.Api.Data;
using StatsHub.Api.Leagues;
using StatsHub.Api.Models;
using StatsHub.Api.Scoresheets;
using static StatsHub.Api.Scoresheets.SwishMatching;

namespace StatsHub.Api.Services
{
    public interface IScoresheetStatsService
    {
        Task FillAsync(Guid linkId, CancellationToken ct = default);
        Task<List<Guid>> LinksAwaitingScoresheetsAsync(DateTime now);
    }

    // A linked player's line in each finished game, read off the game's digital
    // scoresheet (Swish) - for the leagues that publish no box score, which is
    // most of them. What the sheet says is always kept (ScoresheetLine); it
    // becomes the game's stats only when the parent recorded none, since the
    // parent's own numbers win. The newest sheet also gives the player's jersey
    // number on that team.
    public class ScoresheetStatsService : IScoresheetStatsService
    {
        // Sheets are read this many at a time.
        private const int Downloads = 4;
        // A game is watched for its sheet from its end until this long after.
        private static readonly TimeSpan GameLength = TimeSpan.FromMinutes(75);
        private static readonly TimeSpan SheetWatchWindow = TimeSpan.FromHours(24);
        // Parents hear about a sheet only for a recent game - not about every
        // old game of a player who was just linked.
        private static readonly TimeSpan NotifyWithin = TimeSpan.FromDays(2);

        private readonly AppDbContext _context;
        private readonly SwishClient _swish;
        private readonly IPushNotificationService _push;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ScoresheetStatsService> _logger;

        public ScoresheetStatsService(AppDbContext context, SwishClient swish, IPushNotificationService push, IMemoryCache cache, ILogger<ScoresheetStatsService> logger)
        {
            _context = context;
            _swish = swish;
            _push = push;
            _cache = cache;
            _logger = logger;
        }

        // Linked players with a game that ended in the last day and has no
        // line from its sheet yet - the sheet is usually up within hours.
        public async Task<List<Guid>> LinksAwaitingScoresheetsAsync(DateTime now)
        {
            if (!_swish.IsConfigured) return new List<Guid>();
            var (from, to) = (now - GameLength - SheetWatchWindow, now - GameLength);
            var games = await _context.Games
                .Where(g => g.IbbaGameCode != null && g.Status == "Completed" && g.GameDate >= from && g.GameDate <= to)
                .Select(g => new { g.Id, g.HomeTeamId, g.AwayTeamId })
                .ToListAsync();
            if (games.Count == 0) return new List<Guid>();

            var teamIds = games.SelectMany(g => new[] { g.HomeTeamId, g.AwayTeamId }).OfType<Guid>().Distinct().ToList();
            var gameIds = games.Select(g => g.Id).ToList();
            var members = await _context.PlayerIbbaTeams
                .Where(pit => teamIds.Contains(pit.IbbaTeamId))
                .Select(pit => new { pit.PlayerIbbaLinkId, pit.PlayerIbbaLink.PlayerId, pit.IbbaTeamId })
                .ToListAsync();
            var playerIds = members.Select(m => m.PlayerId).Distinct().ToList();
            var have = (await _context.ScoresheetLines
                .Where(l => gameIds.Contains(l.GameId) && playerIds.Contains(l.PlayerId))
                .Select(l => new { l.GameId, l.PlayerId })
                .ToListAsync()).Select(l => (l.GameId, l.PlayerId)).ToHashSet();

            return members
                .Where(m => games.Any(g => (g.HomeTeamId == m.IbbaTeamId || g.AwayTeamId == m.IbbaTeamId)
                    && !have.Contains((g.Id, m.PlayerId)) && !Absent(g.Id, m.PlayerId)))
                .Select(m => m.PlayerIbbaLinkId)
                .Distinct()
                .ToList();
        }

        // A sheet whose name fits two players isn't read again for a week.
        private bool Absent(Guid gameId, Guid playerId) => _cache.TryGetValue(AbsentKey(gameId, playerId), out _);
        private static string AbsentKey(Guid gameId, Guid playerId) => $"sheet-absent:{gameId}:{playerId}";

        public async Task FillAsync(Guid linkId, CancellationToken ct = default)
        {
            if (!_swish.IsConfigured) return;
            var link = await _context.PlayerIbbaLinks.Include(l => l.Player).FirstOrDefaultAsync(l => l.Id == linkId, ct);
            if (link == null) return;
            var player = link.Player;
            var playerName = $"{player.FirstName} {player.LastName}".Trim();

            // The player's IBBA teams that their app team follows - the ones
            // whose games are in the app.
            var memberships = await _context.PlayerTeams
                .Where(pt => pt.PlayerId == player.Id && pt.Team.IbbaTeamId != null)
                .Select(pt => new { Membership = pt, IbbaTeamId = pt.Team.IbbaTeamId!.Value })
                .ToListAsync(ct);
            var ownIds = await _context.PlayerIbbaTeams.Where(pit => pit.PlayerIbbaLinkId == link.Id).Select(pit => pit.IbbaTeamId).ToListAsync(ct);
            ownIds = ownIds.Where(id => memberships.Any(m => m.IbbaTeamId == id)).ToList();
            if (ownIds.Count == 0) return;
            var teams = await _context.IbbaTeams.Where(t => ownIds.Contains(t.Id)).ToListAsync(ct);

            // Stats once made from a sheet the player never entered on - he didn't play.
            var notPlayed = await _context.GameStats
                .Where(gs => gs.PlayerId == player.Id && gs.Source == GameStats.ScoresheetSource
                    && _context.ScoresheetLines.Any(l => l.GameId == gs.GameId && l.PlayerId == gs.PlayerId && !l.Played))
                .ToListAsync(ct);
            _context.GameStats.RemoveRange(notPlayed);

            var seasonStart = LeagueSeason.StartOf(DateTime.UtcNow);
            List<SwishCompetition>? competitions = null;
            foreach (var team in teams)
            {
                var games = await _context.Games
                    .Where(g => g.IbbaGameCode != null && g.Status == "Completed" && g.GameDate >= seasonStart
                        && (g.HomeTeamId == team.Id || g.AwayTeamId == team.Id)
                        && !_context.ScoresheetLines.Any(l => l.GameId == g.Id && l.PlayerId == player.Id))
                    .ToListAsync(ct);
                games = games.Where(g => !Absent(g.Id, player.Id)).ToList();
                if (games.Count > 0)
                {
                    competitions ??= await _swish.CompetitionsAsync(ct);
                    await FillTeamAsync(link, playerName, team, games, competitions, ct);
                    await _context.SaveChangesAsync(ct);
                }

                foreach (var m in memberships.Where(m => m.IbbaTeamId == team.Id))
                    await UpdateJerseyAsync(m.Membership, team.Id, ct);
            }
            await _context.SaveChangesAsync(ct);
        }

        private async Task FillTeamAsync(PlayerIbbaLink link, string playerName, IbbaTeam team, List<Game> games,
            List<SwishCompetition> competitions, CancellationToken ct)
        {
            // Each game's Swish id - known from an earlier sync, else found in
            // the calendars of the team's league and its age group's cups, by
            // the opponent and the date.
            var opponentIds = games.Select(g => g.HomeTeamId == team.Id ? g.AwayTeamId : g.HomeTeamId).OfType<Guid>().Distinct().ToList();
            var opponents = await _context.IbbaTeams.Where(t => opponentIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            if (games.Any(g => g.SwishMatchId == null))
            {
                var (league, cups) = CompetitionsFor(competitions, team.LeagueName);
                var swishGames = new List<SwishGame>();
                foreach (var competition in cups.Prepend(league).OfType<SwishCompetition>())
                    foreach (var group in competition.Groups ?? new())
                    {
                        var calendar = await _swish.CalendarAsync(group.Id, ct);
                        swishGames.AddRange((calendar.Rounds ?? new()).SelectMany(r => r.Matches ?? new())
                            .Where(s => SameTeam(s.HomeTeam.Name, team.Name) || SameTeam(s.AwayTeam.Name, team.Name)));
                    }
                foreach (var game in games.Where(g => g.SwishMatchId == null))
                {
                    var opponentId = game.HomeTeamId == team.Id ? game.AwayTeamId : game.HomeTeamId;
                    if (opponentId is not Guid id || !opponents.TryGetValue(id, out var opponent)) continue;
                    var home = game.HomeTeamId == team.Id;
                    game.SwishMatchId = swishGames.FirstOrDefault(s =>
                        Math.Abs((s.Date.ToUniversalTime() - game.GameDate).TotalHours) < 20
                        && SameTeam(home ? s.HomeTeam.Name : s.AwayTeam.Name, team.Name)
                        && SameTeam(home ? s.AwayTeam.Name : s.HomeTeam.Name, opponent))?.Id;
                }
            }

            // The sheets, a few at a time; then each read on its own (one DbContext).
            var withId = games.Where(g => g.SwishMatchId != null).ToList();
            var sheets = new Dictionary<Guid, ParsedScoresheet?>();
            using (var throttle = new SemaphoreSlim(Downloads))
            {
                var reads = await Task.WhenAll(withId.Select(async g =>
                {
                    await throttle.WaitAsync(ct);
                    try { return (g.Id, Sheet: await _swish.ScoresheetAsync(g.SwishMatchId!, ct)); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogInformation(ex, "Scoresheet of Swish match {MatchId} couldn't be read", g.SwishMatchId);
                        return (g.Id, Sheet: (ParsedScoresheet?)null);
                    }
                    finally { throttle.Release(); }
                }));
                foreach (var (id, sheet) in reads) sheets[id] = sheet;
            }

            foreach (var game in withId)
            {
                if (sheets[game.Id] is not { } sheet) continue; // not up yet
                var home = game.HomeTeamId == team.Id;
                var opponentName = (home ? game.AwayTeamId : game.HomeTeamId) is Guid oid && opponents.TryGetValue(oid, out var n) ? n : "the opponent";
                // The sheet's team A is the home team; by name in case it differs.
                var side = SameTeam(sheet.Home.Name, team.Name) ? sheet.Home
                    : SameTeam(sheet.Away.Name, team.Name) ? sheet.Away
                    : home ? sheet.Home : sheet.Away;

                var found = link.ScoresheetCode is { } code ? side.Players.FirstOrDefault(p => p.Code == code) : null;
                if (found == null)
                {
                    var (status, match, _) = FindPlayer(playerName, side.Players);
                    // Not on the sheet: kept, so the game says he wasn't listed
                    // (and isn't read again). Two who fit: asked again in a week.
                    if (status == ScoresheetMatchStatus.NotFound)
                    {
                        _context.ScoresheetLines.Add(new ScoresheetLine
                        {
                            GameId = game.Id,
                            PlayerId = link.PlayerId,
                            NotListed = true,
                            SheetValid = sheet.IsValid,
                        });
                        continue;
                    }
                    if (status != ScoresheetMatchStatus.Matched)
                    {
                        _cache.Set(AbsentKey(game.Id, link.PlayerId), true, TimeSpan.FromDays(7));
                        continue;
                    }
                    found = match!;
                    link.ScoresheetCode ??= found.Code;
                }
                await SaveLineAsync(link.PlayerId, game, sheet, found, opponentName, ct);
            }
        }

        private async Task SaveLineAsync(Guid playerId, Game game, ParsedScoresheet sheet, ScoresheetPlayer p, string opponentName, CancellationToken ct)
        {
            var line = new ScoresheetLine
            {
                GameId = game.Id,
                PlayerId = playerId,
                Number = p.Number,
                Code = p.Code,
                Played = p.Played,
                Starter = p.Starter,
                Points = p.Points,
                FreeThrows = p.FreeThrows,
                TwoPointers = p.TwoPointers,
                ThreePointers = p.ThreePointers,
                Fouls = p.Fouls,
                SheetValid = sheet.IsValid,
                SheetErrors = sheet.IsValid ? null : string.Join("; ", sheet.Errors.Take(3)),
            };
            _context.ScoresheetLines.Add(line);

            // The game's stats: the sheet's, unless someone else's are there -
            // the parent's own (which win) or a league box score (which has more).
            // On the sheet but never entered (no X): didn't play - no stats.
            var stats = await _context.GameStats.FirstOrDefaultAsync(gs => gs.GameId == game.Id && gs.PlayerId == playerId, ct);
            string? news = null;
            if (!line.Played)
            {
                if (stats?.Source == GameStats.ScoresheetSource) _context.GameStats.Remove(stats);
            }
            else if (stats == null)
            {
                _context.GameStats.Add(FromSheet(new GameStats { GameId = game.Id, PlayerId = playerId }, line));
                news = "none";
            }
            else if (stats.Source == GameStats.ScoresheetSource)
                FromSheet(stats, line);
            else if (stats.Source == null && !Agrees(stats, line))
                news = "differs";

            if (news != null && DateTime.UtcNow - game.GameDate < NotifyWithin)
                await NotifyAsync(playerId, game, line, news == "differs" ? stats : null, opponentName);
        }

        // Made shots and fouls - the rest a sheet doesn't have.
        internal static GameStats FromSheet(GameStats stats, ScoresheetLine line)
        {
            stats.Source = GameStats.ScoresheetSource;
            stats.FieldGoalsMade = line.TwoPointers;
            stats.ThreePointersMade = line.ThreePointers;
            stats.FreeThrowsMade = line.FreeThrows;
            stats.Fouls = line.Fouls;
            stats.FieldGoalsAttempted = stats.ThreePointersAttempted = stats.FreeThrowsAttempted = null;
            stats.OffensiveRebounds = stats.DefensiveRebounds = null;
            stats.Assists = stats.Steals = stats.Blocks = stats.Turnovers = null;
            stats.MinutesPlayed = null;
            stats.UpdatedAt = DateTime.UtcNow;
            return stats;
        }

        // The parent's numbers and the sheet's say the same - the scoring and the fouls.
        internal static bool Agrees(GameStats stats, ScoresheetLine line) =>
            stats.FieldGoalsMade == line.TwoPointers && stats.ThreePointersMade == line.ThreePointers && stats.FreeThrowsMade == line.FreeThrows
            && stats.Fouls == line.Fouls;

        private async Task NotifyAsync(Guid playerId, Game game, ScoresheetLine line, GameStats? recorded, string opponentName)
        {
            var player = await _context.Players.Include(p => p.Parents).FirstAsync(p => p.Id == playerId);
            var followers = player.Parents.Select(p => p.UserId)
                .Concat(player.LinkedUserId is Guid own ? [own] : [])
                .Distinct()
                .ToList();
            var sheet = $"{line.Points} pts ({line.TwoPointers}×2, {line.ThreePointers}×3, {line.FreeThrows} FT)";
            var warning = line.SheetValid ? "" : " · the sheet has errors, check it";
            var (title, body) = recorded == null
                ? ($"📋 {player.FirstName}'s scoresheet vs {opponentName}", $"#{line.Number}: {sheet}{warning}")
                : ($"📋 The scoresheet differs from your stats", $"{player.FirstName} vs {opponentName}: sheet {sheet}, you recorded {recorded.TotalPoints} pts{warning}");
            try
            {
                await _push.NotifyUsersAsync(followers, title, body, $"/games/{game.Id}?playerId={playerId}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scoresheet push for game {GameId} failed", game.Id);
            }
        }

        // The number from the team's newest sheet - unless the parent set one.
        private async Task UpdateJerseyAsync(PlayerTeam membership, Guid ibbaTeamId, CancellationToken ct)
        {
            if (membership.JerseyNumber != 0 && !membership.JerseyFromScoresheet) return;
            var newest = await _context.ScoresheetLines
                .Where(l => l.PlayerId == membership.PlayerId && !l.NotListed && (l.Game.HomeTeamId == ibbaTeamId || l.Game.AwayTeamId == ibbaTeamId))
                .OrderByDescending(l => l.Game.GameDate)
                .Select(l => (int?)l.Number)
                .FirstOrDefaultAsync(ct);
            if (newest is not int number) return;
            membership.JerseyNumber = number;
            membership.JerseyFromScoresheet = true;
        }
    }
}
