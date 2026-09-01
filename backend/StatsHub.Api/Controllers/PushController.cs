using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PushController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ICurrentUserService _currentUser;
        private readonly IConfiguration _configuration;
        private readonly IPushNotificationService _push;

        public PushController(AppDbContext context, ICurrentUserService currentUser, IConfiguration configuration, IPushNotificationService push)
        {
            _context = context;
            _currentUser = currentUser;
            _configuration = configuration;
            _push = push;
        }

        // Public: the frontend needs this before the user has necessarily done
        // anything auth-gated, to build the PushManager.subscribe() call.
        [HttpGet("vapid-public-key")]
        public ActionResult<string> GetVapidPublicKey()
        {
            var key = _configuration["Vapid:PublicKey"];
            if (string.IsNullOrEmpty(key)) return NotFound(new { message = "Push notifications are not configured on this server" });
            return Ok(key);
        }

        [HttpPost("subscribe")]
        [Authorize]
        public async Task<ActionResult> Subscribe([FromBody] SubscribePushDto dto)
        {
            var existing = await _context.PushSubscriptions.FirstOrDefaultAsync(ps => ps.Endpoint == dto.Endpoint);
            if (existing != null)
            {
                // Same browser subscribing again (e.g. after a re-login) - keep one
                // row per endpoint and just make sure it points at the current user.
                existing.UserId = _currentUser.UserId;
                existing.P256dh = dto.P256dh;
                existing.Auth = dto.Auth;
            }
            else
            {
                _context.PushSubscriptions.Add(new Models.PushSubscription
                {
                    UserId = _currentUser.UserId,
                    Endpoint = dto.Endpoint,
                    P256dh = dto.P256dh,
                    Auth = dto.Auth,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("test")]
        [Authorize]
        public async Task<ActionResult> SendTest()
        {
            await _push.NotifyUsersAsync(new[] { _currentUser.UserId }, "🔔 Test notification", "Push notifications are working!", "/");
            return NoContent();
        }

        [HttpPost("unsubscribe")]
        [Authorize]
        public async Task<ActionResult> Unsubscribe([FromBody] UnsubscribePushDto dto)
        {
            var existing = await _context.PushSubscriptions
                .FirstOrDefaultAsync(ps => ps.Endpoint == dto.Endpoint && ps.UserId == _currentUser.UserId);
            if (existing != null)
            {
                _context.PushSubscriptions.Remove(existing);
                await _context.SaveChangesAsync();
            }
            return NoContent();
        }
    }
}
