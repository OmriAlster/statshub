using StatsHub.Api.IbbaScraping;

namespace StatsHub.Api.Services
{
    // Results soon after the final whistle, not at the next nightly sync:
    // every 15 minutes, each followed IBBA team with a game that should be
    // over (and no result yet) has its games re-read - so the score and the
    // "Final score" push arrive within about a quarter of an hour of IBBA
    // posting them. Only those teams, one at a time.
    public class IbbaResultWatcherBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan PauseBetweenTeams = TimeSpan.FromSeconds(3);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<IbbaResultWatcherBackgroundService> _logger;

        public IbbaResultWatcherBackgroundService(IServiceScopeFactory scopeFactory, ILogger<IbbaResultWatcherBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    await CheckAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "IBBA result check failed");
                }
            }
            while (await WaitAsync(timer, stoppingToken));
        }

        private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
        {
            try { return await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { return false; }
        }

        private async Task CheckAsync(CancellationToken stoppingToken)
        {
            List<Guid> teamIds;
            using (var scope = _scopeFactory.CreateScope())
                teamIds = await scope.ServiceProvider.GetRequiredService<IIbbaService>().TeamsAwaitingResultsAsync(DateTime.UtcNow);
            if (teamIds.Count == 0) return;

            _logger.LogInformation("Checking IBBA for results of {Count} teams", teamIds.Count);
            foreach (var teamId in teamIds)
            {
                // A fresh scope (and DbContext) per team, like the nightly sync.
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IIbbaService>().RefreshTeamGamesAsync(teamId);
                }
                catch (IbbaLayoutException ex)
                {
                    _logger.LogError("IBBA layout changed: {Details}", ex.Details);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "IBBA result check failed for team {TeamId}", teamId);
                }
                await Task.Delay(PauseBetweenTeams, stoppingToken);
            }
            MemoryRelief.ReleaseSoon();
        }
    }
}
