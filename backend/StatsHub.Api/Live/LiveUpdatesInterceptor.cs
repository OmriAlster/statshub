using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StatsHub.Api.Data;
using StatsHub.Api.Models;

namespace StatsHub.Api.Live
{
    // Every save that touches a game, a box score or a shot - from any part
    // of the app, including an IBBA sync posting a final score - tells the
    // screens watching it (LiveHub) once the save has gone through. Watching
    // the database instead of each write endpoint means a new way of changing
    // a game can't forget to notify.
    public class LiveUpdatesInterceptor : SaveChangesInterceptor
    {
        private readonly IHubContext<LiveHub> _hub;
        private readonly ILogger<LiveUpdatesInterceptor> _logger;

        // What each context is about to save, until the save completes. A
        // DbContext runs one save at a time, so one entry per context.
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, PendingChanges> _pending = new();

        private sealed class PendingChanges
        {
            public HashSet<Guid> GameIds { get; } = new();
            public HashSet<Guid> GameStatsIds { get; } = new();
        }

        public LiveUpdatesInterceptor(IHubContext<LiveHub> hub, ILogger<LiveUpdatesInterceptor> logger)
        {
            _hub = hub;
            _logger = logger;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Collect(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Collect(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            await NotifyAsync(eventData.Context);
            return await base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            NotifyAsync(eventData.Context).GetAwaiter().GetResult();
            return base.SavedChanges(eventData, result);
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            if (eventData.Context != null) _pending.Remove(eventData.Context);
        }

        private void Collect(DbContext? context)
        {
            if (context == null) return;
            var pending = new PendingChanges();
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
                switch (entry.Entity)
                {
                    case Game game: pending.GameIds.Add(game.Id); break;
                    case GameStats stats: pending.GameIds.Add(stats.GameId); break;
                    case Shot shot: pending.GameStatsIds.Add(shot.GameStatsId); break;
                }
            }
            if (pending.GameIds.Count + pending.GameStatsIds.Count > 0) _pending.AddOrUpdate(context, pending);
            else _pending.Remove(context);
        }

        private async Task NotifyAsync(DbContext? context)
        {
            if (context is not AppDbContext db || !_pending.TryGetValue(db, out var pending)) return;
            _pending.Remove(db);

            try
            {
                var gameIds = pending.GameIds;
                if (pending.GameStatsIds.Count > 0)
                {
                    var statsIds = pending.GameStatsIds.ToList();
                    gameIds.UnionWith(await db.GameStats.Where(gs => statsIds.Contains(gs.Id)).Select(gs => gs.GameId).ToListAsync());
                }
                var ids = gameIds.ToList();

                // The players each game matters to: those with a box score in
                // it, and those on an app team it belongs to (its own team for
                // a manual game, a team linked to either side for an IBBA one -
                // see AccessRules.TeamsForGame). Three queries for any number
                // of games, since an IBBA sync can change dozens at once.
                var withStats = await db.GameStats.Where(gs => ids.Contains(gs.GameId)).Select(gs => new { gs.GameId, gs.PlayerId }).ToListAsync();
                var games = await db.Games.AsNoTracking().Where(g => ids.Contains(g.Id))
                    .Select(g => new { g.Id, g.IbbaGameCode, g.HomeTeamId, g.AwayTeamId }).ToListAsync();
                var sideIds = games.SelectMany(g => new[] { g.HomeTeamId, g.AwayTeamId }).OfType<Guid>().Distinct().ToList();
                var teams = await db.Teams.Where(t => sideIds.Contains(t.Id) || (t.IbbaTeamId != null && sideIds.Contains(t.IbbaTeamId.Value)))
                    .Select(t => new { t.Id, t.IbbaTeamId, Players = t.PlayerTeams.Select(pt => pt.PlayerId).ToList() }).ToListAsync();

                foreach (var gameId in ids)
                {
                    var players = withStats.Where(s => s.GameId == gameId).Select(s => s.PlayerId).ToList();
                    var game = games.FirstOrDefault(g => g.Id == gameId);
                    if (game != null)
                    {
                        var sides = new[] { game.HomeTeamId, game.AwayTeamId }.OfType<Guid>().ToHashSet();
                        players.AddRange(teams
                            .Where(t => game.IbbaGameCode == null ? t.Id == (game.HomeTeamId ?? game.AwayTeamId) : t.IbbaTeamId is Guid ibba && sides.Contains(ibba))
                            .SelectMany(t => t.Players));
                    }

                    var groups = players.Distinct().Select(LiveHub.PlayerGroup).Append(LiveHub.GameGroup(gameId)).ToList();
                    await _hub.Clients.Groups(groups).SendAsync(LiveHub.GameChanged, gameId);
                }
            }
            catch (Exception ex)
            {
                // Watchers fall back to polling; never fail the save over it.
                _logger.LogWarning(ex, "Couldn't send live updates");
            }
        }
    }
}
