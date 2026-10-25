using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;

namespace StatsHub.Api.Live
{
    // Tells watching screens the moment a game changes - the score, a box
    // score, a shot, the status, who's on court - so they reload it right
    // away instead of polling every few seconds. Only "game X changed" is
    // sent (see LiveUpdatesInterceptor); each screen reloads the game itself
    // through the API, with its own access and its own team's point of view.
    public class LiveHub : Hub
    {
        public const string Path = "/hubs/live";
        public const string GameChanged = "GameChanged";

        public static string PlayerGroup(Guid playerId) => $"player:{playerId}";
        public static string GameGroup(Guid gameId) => $"game:{gameId}";

        private readonly AppDbContext _db;
        public LiveHub(AppDbContext db) => _db = db;

        // A signed-in parent (or the player themself) watching a player's games.
        public async Task WatchPlayer(Guid playerId)
        {
            if (!Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || !await _db.CanReadPlayerAsync(playerId, userId))
                throw new HubException("Not allowed to watch this player.");
            await Groups.AddToGroupAsync(Context.ConnectionId, PlayerGroup(playerId));
        }

        public Task UnwatchPlayer(Guid playerId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, PlayerGroup(playerId));

        // Anyone with a share link: the player's games, or the one shared game.
        public async Task WatchShare(string token)
        {
            var now = DateTime.UtcNow;
            var link = await _db.ShareLinks
                .Where(l => l.Token == token && (l.ExpiresAt == null || l.ExpiresAt > now))
                .Select(l => new { l.PlayerId, l.GameId })
                .FirstOrDefaultAsync()
                ?? throw new HubException("This share link isn't valid.");
            await Groups.AddToGroupAsync(Context.ConnectionId, link.GameId is Guid gameId ? GameGroup(gameId) : PlayerGroup(link.PlayerId));
        }
    }
}
