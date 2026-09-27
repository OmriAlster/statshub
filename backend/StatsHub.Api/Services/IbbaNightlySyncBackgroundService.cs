using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;

namespace StatsHub.Api.Services
{
    // Syncs every IBBA-linked player once a night at midnight Israel time, so
    // schedules, results and standings stay current without anyone pressing
    // "Sync". Same sync as the manual button - including its push
    // notifications for new, rescheduled and finished games.
    public class IbbaNightlySyncBackgroundService : BackgroundService
    {
        // Players are synced one at a time with a short pause in between, so
        // the job doesn't hammer ibasketball.co.il with a burst of requests.
        private static readonly TimeSpan PauseBetweenPlayers = TimeSpan.FromSeconds(3);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<IbbaNightlySyncBackgroundService> _logger;
        private readonly TimeZoneInfo _israelTime;

        public IbbaNightlySyncBackgroundService(IServiceScopeFactory scopeFactory, ILogger<IbbaNightlySyncBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _israelTime = FindIsraelTimeZone(logger);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeUntilNextMidnight();
                _logger.LogInformation("Next nightly IBBA sync in {Delay}", delay);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    return; // shutting down
                }

                try
                {
                    await SyncAllAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nightly IBBA sync failed");
                }
            }
        }

        private async Task SyncAllAsync(CancellationToken stoppingToken)
        {
            List<int> linkIds;
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                linkIds = await context.PlayerIbbaLinks.OrderBy(l => l.Id).Select(l => l.Id).ToListAsync(stoppingToken);
            }

            _logger.LogInformation("Nightly IBBA sync starting for {Count} linked players", linkIds.Count);

            var failed = 0;
            foreach (var linkId in linkIds)
            {
                if (stoppingToken.IsCancellationRequested) return;

                // A fresh scope (and DbContext) per player, so one player's
                // sync never leaves tracked entities or a broken context behind
                // for the next one.
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var ibba = scope.ServiceProvider.GetRequiredService<IIbbaService>();
                    await ibba.SyncLinkForScheduledJobAsync(linkId);
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Nightly IBBA sync failed for link {LinkId}", linkId);
                }

                try
                {
                    await Task.Delay(PauseBetweenPlayers, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }

            _logger.LogInformation("Nightly IBBA sync finished: {Count} players, {Failed} failed", linkIds.Count, failed);
        }

        private TimeSpan TimeUntilNextMidnight()
        {
            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _israelTime);
            var nextMidnightLocal = DateTime.SpecifyKind(nowLocal.Date.AddDays(1), DateTimeKind.Unspecified);
            var nextMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(nextMidnightLocal, _israelTime);
            var delay = nextMidnightUtc - nowUtc;
            return delay > TimeSpan.Zero ? delay : TimeSpan.FromMinutes(1);
        }

        // "Asia/Jerusalem" is the IANA id (Linux, and Windows with ICU);
        // "Israel Standard Time" is the Windows id. If neither is available,
        // fall back to UTC rather than fail to start - the sync then runs at
        // midnight UTC (02:00/03:00 in Israel), which still does its job.
        private static TimeZoneInfo FindIsraelTimeZone(ILogger logger)
        {
            foreach (var id in new[] { "Asia/Jerusalem", "Israel Standard Time" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }

            logger.LogWarning("Israel time zone not found - nightly IBBA sync will run at midnight UTC");
            return TimeZoneInfo.Utc;
        }
    }
}
