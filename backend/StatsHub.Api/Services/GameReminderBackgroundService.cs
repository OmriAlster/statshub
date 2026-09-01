using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;

namespace StatsHub.Api.Services
{
    // Polls every few minutes for games starting within the next hour that
    // haven't been reminded about yet, and pushes a "starting soon" notice to
    // that team's parents/players. The project's first background job - there's
    // no other scheduled infrastructure to hook into, so this owns its own timer.
    public class GameReminderBackgroundService : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ReminderWindow = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<GameReminderBackgroundService> _logger;

        public GameReminderBackgroundService(IServiceScopeFactory scopeFactory, ILogger<GameReminderBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SendDueRemindersAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Game reminder sweep failed");
                }

                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Shutting down.
                }
            }
        }

        private async Task SendDueRemindersAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var push = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

            var now = DateTime.UtcNow;
            var games = await context.Games
                .Where(g => g.Status == "Upcoming"
                    && g.ReminderSentAt == null
                    && g.GameDate > now
                    && g.GameDate <= now + ReminderWindow)
                .ToListAsync(stoppingToken);

            foreach (var game in games)
            {
                // Mark sent up front - if the push itself fails partway through
                // fan-out, better to miss a reminder than spam one every 5 minutes.
                game.ReminderSentAt = now;
                await context.SaveChangesAsync(stoppingToken);

                await push.NotifyTeamAsync(
                    game.TeamId,
                    "⏰ Game starting soon",
                    $"vs {game.OpponentName} at {game.GameDate:h:mm tt}",
                    $"/games/{game.Id}");
            }
        }
    }
}
