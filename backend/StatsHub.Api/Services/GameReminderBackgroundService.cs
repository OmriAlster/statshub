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

            // HomeTeamId/AwayTeamId mean IbbaTeam ids for a synced game - batch
            // the name lookups for all of them up front rather than one query
            // per game (there's no formal nav to Include, see Game.cs).
            var ibbaTeamIds = games.Where(g => g.IbbaGameCode != null)
                .SelectMany(g => new[] { g.HomeTeamId, g.AwayTeamId })
                .Where(id => id.HasValue).Select(id => id!.Value)
                .Distinct().ToList();
            var ibbaTeamNames = ibbaTeamIds.Count > 0
                ? await context.IbbaTeams.Where(t => ibbaTeamIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, stoppingToken)
                : new Dictionary<Guid, string>();

            foreach (var game in games)
            {
                // Mark sent up front - if the push itself fails partway through
                // fan-out, better to miss a reminder than spam one every 5 minutes.
                game.ReminderSentAt = now;
                await context.SaveChangesAsync(stoppingToken);

                if (game.IbbaGameCode != null)
                {
                    // Symmetric - both real teams playing are "starting soon", so
                    // each side's linked app Teams get notified with the other
                    // side's name as the opponent.
                    if (game.HomeTeamId.HasValue)
                    {
                        var awayName = game.AwayTeamId.HasValue && ibbaTeamNames.TryGetValue(game.AwayTeamId.Value, out var an) ? an : string.Empty;
                        await push.NotifyIbbaTeamAsync(
                            game.HomeTeamId.Value,
                            "⏰ Game starting soon",
                            $"vs {awayName} at {{time}}",
                            $"/games/{game.Id}",
                            gameDate: game.GameDate);
                    }
                    if (game.AwayTeamId.HasValue)
                    {
                        var homeName = game.HomeTeamId.HasValue && ibbaTeamNames.TryGetValue(game.HomeTeamId.Value, out var hn) ? hn : string.Empty;
                        await push.NotifyIbbaTeamAsync(
                            game.AwayTeamId.Value,
                            "⏰ Game starting soon",
                            $"vs {homeName} at {{time}}",
                            $"/games/{game.Id}",
                            gameDate: game.GameDate);
                    }
                }
                else
                {
                    var teamId = game.HomeTeamId ?? game.AwayTeamId;
                    if (teamId.HasValue)
                    {
                        await push.NotifyTeamAsync(
                            teamId.Value,
                            "⏰ Game starting soon",
                            $"vs {game.OpponentName} at {{time}}",
                            $"/games/{game.Id}",
                            gameDate: game.GameDate);
                    }
                }
            }
        }
    }
}
