using StatsHub.Api.IbbaScraping;
using StatsHub.Api.Leagues;

namespace StatsHub.Api.Services
{
    // Results soon after the final whistle, not at the next nightly sync:
    // every 15 minutes, each followed IBBA team with a game that should be
    // over (and no result yet) has its games re-read - so the score and the
    // "Final score" push arrive within about a quarter of an hour of IBBA
    // posting them. Only those teams, one at a time. Then, for a day after a
    // game, its scoresheet: the player's line as soon as the sheet is up.
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
            await CheckResultsAsync(stoppingToken);
            await CheckScoresheetsAsync(stoppingToken);
        }

        private async Task CheckScoresheetsAsync(CancellationToken stoppingToken)
        {
            List<Guid> linkIds;
            using (var scope = _scopeFactory.CreateScope())
                linkIds = await scope.ServiceProvider.GetRequiredService<IScoresheetStatsService>().LinksAwaitingScoresheetsAsync(DateTime.UtcNow);
            if (linkIds.Count == 0) return;

            _logger.LogInformation("Checking Swish for the scoresheets of {Count} players", linkIds.Count);
            foreach (var linkId in linkIds)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IScoresheetStatsService>().FillAsync(linkId, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Scoresheet check failed for link {LinkId}", linkId);
                }
            }
            MemoryRelief.ReleaseSoon();
        }

        private async Task CheckResultsAsync(CancellationToken stoppingToken)
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
                catch (LeagueLayoutException ex)
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
