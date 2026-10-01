using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using WebPush;

namespace StatsHub.Api.Services
{
    public interface IPushNotificationService
    {
        // Every parent of every player rostered on this team, plus any of
        // those players who have their own linked login - the same
        // "who cares about this team" set used for edit-access checks
        // elsewhere (GameService.OwnsTeamAsync et al), just enumerated
        // instead of tested against a single user.
        //
        // gameDate, when given, travels as a raw UTC instant in the payload -
        // the server has no idea what timezone the recipient's device is in,
        // so it never formats a date into the body text itself. If body
        // contains the token "{time}" or "{datetime}", the service worker
        // replaces it with that instant formatted in the device's own local
        // time before the notification is shown.
        Task NotifyTeamAsync(Guid teamId, string title, string body, string? url = null, Guid? excludeUserId = null, DateTime? gameDate = null);

        // Same as NotifyTeamAsync, but for a shared real-world IBBA team - every
        // app Team currently linked to it (one per family tracking a kid on that
        // team) gets notified, not just whichever one happens to own the game
        // row. Use this for anything about an IBBA-synced game; NotifyTeamAsync
        // stays right for a manually-created game, which is always single-team.
        Task NotifyIbbaTeamAsync(Guid ibbaTeamId, string title, string body, string? url = null, Guid? excludeUserId = null, DateTime? gameDate = null);
        Task NotifyUsersAsync(IEnumerable<Guid> userIds, string title, string body, string? url = null, DateTime? gameDate = null);
    }

    public class PushNotificationService : IPushNotificationService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PushNotificationService> _logger;
        private readonly VapidDetails? _vapidDetails;

        public PushNotificationService(AppDbContext context, IConfiguration configuration, ILogger<PushNotificationService> logger)
        {
            _context = context;
            _logger = logger;

            var publicKey = configuration["Vapid:PublicKey"];
            var privateKey = configuration["Vapid:PrivateKey"];
            var subject = configuration["Vapid:Subject"];

            // Left unset in an environment with no keys configured (e.g. a fresh
            // dev checkout before generating a local pair) - every send becomes a
            // harmless no-op with a log line instead of throwing.
            if (!string.IsNullOrEmpty(publicKey) && !string.IsNullOrEmpty(privateKey) && !string.IsNullOrEmpty(subject))
            {
                _vapidDetails = new VapidDetails(subject, publicKey, privateKey);
            }
        }

        public async Task NotifyTeamAsync(Guid teamId, string title, string body, string? url = null, Guid? excludeUserId = null, DateTime? gameDate = null)
        {
            var parentUserIds = await _context.PlayerTeams
                .Where(pt => pt.TeamId == teamId)
                .SelectMany(pt => pt.Player.Parents.Select(pp => pp.UserId))
                .ToListAsync();

            var linkedPlayerUserIds = await _context.PlayerTeams
                .Where(pt => pt.TeamId == teamId && pt.Player.LinkedUserId != null)
                .Select(pt => pt.Player.LinkedUserId!.Value)
                .ToListAsync();

            var recipientUserIds = parentUserIds.Concat(linkedPlayerUserIds).Distinct();
            if (excludeUserId.HasValue) recipientUserIds = recipientUserIds.Where(id => id != excludeUserId.Value);

            await NotifyUsersAsync(recipientUserIds, title, body, url, gameDate);
        }

        public async Task NotifyIbbaTeamAsync(Guid ibbaTeamId, string title, string body, string? url = null, Guid? excludeUserId = null, DateTime? gameDate = null)
        {
            var parentUserIds = await _context.PlayerTeams
                .Where(pt => pt.Team.IbbaTeamId == ibbaTeamId)
                .SelectMany(pt => pt.Player.Parents.Select(pp => pp.UserId))
                .ToListAsync();

            var linkedPlayerUserIds = await _context.PlayerTeams
                .Where(pt => pt.Team.IbbaTeamId == ibbaTeamId && pt.Player.LinkedUserId != null)
                .Select(pt => pt.Player.LinkedUserId!.Value)
                .ToListAsync();

            var recipientUserIds = parentUserIds.Concat(linkedPlayerUserIds).Distinct();
            if (excludeUserId.HasValue) recipientUserIds = recipientUserIds.Where(id => id != excludeUserId.Value);

            await NotifyUsersAsync(recipientUserIds, title, body, url, gameDate);
        }

        public async Task NotifyUsersAsync(IEnumerable<Guid> userIds, string title, string body, string? url = null, DateTime? gameDate = null)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return;

            if (_vapidDetails == null)
            {
                _logger.LogInformation("Push notification suppressed (VAPID keys not configured): {Title}", title);
                return;
            }

            var subscriptions = await _context.PushSubscriptions.Where(ps => ids.Contains(ps.UserId)).ToListAsync();
            if (subscriptions.Count == 0) return;

            var payload = JsonSerializer.Serialize(new { title, body, url, gameDateIso = gameDate?.ToString("O") });
            var client = new WebPushClient();
            var expired = new List<Models.PushSubscription>();

            foreach (var sub in subscriptions)
            {
                try
                {
                    await client.SendNotificationAsync(
                        new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth),
                        payload,
                        _vapidDetails);
                }
                catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                {
                    // The browser/OS dropped this subscription (uninstall, permission
                    // revoked, etc.) - stop trying to reach it.
                    expired.Add(sub);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Push notification failed for subscription {Id}", sub.Id);
                }
            }

            if (expired.Count > 0)
            {
                _context.PushSubscriptions.RemoveRange(expired);
                await _context.SaveChangesAsync();
            }
        }
    }
}
