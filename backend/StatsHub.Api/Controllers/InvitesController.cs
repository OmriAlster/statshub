using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.Models;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    // Family invites: a parent picks who they're inviting (another parent or
    // family member, or their child), sends the link, and whoever opens it
    // signs in and is in - no code to type.
    [ApiController]
    [Route("api/invites")]
    [Authorize]
    public class InvitesController : ControllerBase
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

        private readonly AppDbContext _context;
        private readonly ICurrentUserService _currentUser;

        public InvitesController(AppDbContext context, ICurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public class CreateInviteDto
        {
            public string Kind { get; set; } = FamilyInvite.ParentKind;
            public List<Guid> PlayerIds { get; set; } = new();
        }

        public class InvitePlayerDto
        {
            public Guid Id { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string? ProfilePictureUrl { get; set; }
        }

        public class InviteDto
        {
            public Guid Id { get; set; }
            public string Code { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public DateTime ExpiresAt { get; set; }
            public List<InvitePlayerDto> Players { get; set; } = new();
            public List<string> Joined { get; set; } = new();
        }

        // What the link's page shows before anyone signs in.
        public class InvitePreviewDto
        {
            public string Kind { get; set; } = string.Empty;
            public string InvitedBy { get; set; } = string.Empty;
            // open, expired or used
            public string Status { get; set; } = "open";
            public List<InvitePlayerDto> Players { get; set; } = new();
        }

        public class AcceptResultDto
        {
            public string Kind { get; set; } = string.Empty;
            public List<Guid> PlayerIds { get; set; } = new();
        }

        private IQueryable<FamilyInvite> Invites() => _context.FamilyInvites
            .Include(i => i.CreatedBy)
            .Include(i => i.Players).ThenInclude(ip => ip.Player)
            .Include(i => i.Joins).ThenInclude(j => j.User);

        private static InvitePlayerDto PlayerOf(Player p) => new()
        {
            Id = p.Id, FirstName = p.FirstName, LastName = p.LastName, ProfilePictureUrl = p.ProfilePictureUrl,
        };

        private static InviteDto ToDto(FamilyInvite i) => new()
        {
            Id = i.Id,
            Code = i.Code,
            Kind = i.Kind,
            ExpiresAt = i.ExpiresAt,
            Players = i.Players.OrderBy(ip => ip.Player.CreatedAt).Select(ip => PlayerOf(ip.Player)).ToList(),
            Joined = i.Joins.OrderBy(j => j.JoinedAt).Select(j => j.User.FirstName).ToList(),
        };

        private static string Normalize(string code) => code.Trim().ToUpperInvariant();

        [HttpPost]
        public async Task<ActionResult<InviteDto>> Create([FromBody] CreateInviteDto dto)
        {
            var userId = _currentUser.UserId;
            var kind = dto.Kind == FamilyInvite.PlayerKind ? FamilyInvite.PlayerKind : dto.Kind == FamilyInvite.ParentKind ? FamilyInvite.ParentKind : null;
            var playerIds = dto.PlayerIds.Distinct().ToList();
            if (kind == null || playerIds.Count == 0 || (kind == FamilyInvite.PlayerKind && playerIds.Count != 1))
                return BadRequest(new { message = "Choose who the invite is for" });

            var mine = await _context.PlayerParents.Where(pp => pp.UserId == userId && playerIds.Contains(pp.PlayerId)).CountAsync();
            if (mine != playerIds.Count) return NotFound(new { message = "Player not found" });

            var now = DateTime.UtcNow;
            var open = await Invites()
                .Where(i => i.CreatedByUserId == userId && i.Kind == kind && i.RevokedAt == null && i.ExpiresAt > now && i.UsedAt == null)
                .ToListAsync();

            if (kind == FamilyInvite.ParentKind)
            {
                // The same kids again: the same link, good for another week.
                var same = open.FirstOrDefault(i => i.Players.Select(ip => ip.PlayerId).ToHashSet().SetEquals(playerIds));
                if (same != null)
                {
                    same.ExpiresAt = now + Lifetime;
                    await _context.SaveChangesAsync();
                    return Ok(ToDto(same));
                }
            }
            else
            {
                // One open link per kid - a new one replaces the last.
                foreach (var old in open.Where(i => i.Players.Any(ip => ip.PlayerId == playerIds[0])))
                    old.RevokedAt = now;
            }

            string code;
            do code = InviteCodeGenerator.Generate();
            while (await _context.FamilyInvites.AnyAsync(i => i.Code == code));

            var invite = new FamilyInvite { Code = code, Kind = kind, CreatedByUserId = userId, CreatedAt = now, ExpiresAt = now + Lifetime };
            foreach (var playerId in playerIds)
                invite.Players.Add(new FamilyInvitePlayer { PlayerId = playerId });
            _context.FamilyInvites.Add(invite);
            await _context.SaveChangesAsync();

            return Ok(ToDto(await Invites().FirstAsync(i => i.Id == invite.Id)));
        }

        // The invites this parent sent that can still be used.
        [HttpGet]
        public async Task<ActionResult<List<InviteDto>>> Mine()
        {
            var userId = _currentUser.UserId;
            var now = DateTime.UtcNow;
            var invites = await Invites()
                .Where(i => i.CreatedByUserId == userId && i.RevokedAt == null && i.ExpiresAt > now && i.UsedAt == null)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
            return invites.Select(ToDto).ToList();
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Revoke(Guid id)
        {
            var invite = await _context.FamilyInvites.FirstOrDefaultAsync(i => i.Id == id && i.CreatedByUserId == _currentUser.UserId);
            if (invite == null) return NotFound();
            invite.RevokedAt ??= DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // Open to anyone with the link - the code is the secret (8 characters
        // of 32: too many to guess, so the app's usual limit is enough), and
        // the page only shows first names and photos.
        [AllowAnonymous]
        [HttpGet("{code}")]
        public async Task<ActionResult<InvitePreviewDto>> Preview(string code)
        {
            var invite = await Invites().FirstOrDefaultAsync(i => i.Code == Normalize(code));
            if (invite == null) return NotFound(new { message = "This invite link doesn't exist" });

            var now = DateTime.UtcNow;
            return new InvitePreviewDto
            {
                Kind = invite.Kind,
                InvitedBy = invite.CreatedBy.FirstName,
                Status = invite.UsedAt != null ? "used" : invite.IsOpenAt(now) ? "open" : "expired",
                Players = invite.Players.OrderBy(ip => ip.Player.CreatedAt).Select(ip => PlayerOf(ip.Player)).ToList(),
            };
        }

        [HttpPost("{code}/accept")]
        public async Task<ActionResult<AcceptResultDto>> Accept(string code)
        {
            var userId = _currentUser.UserId;
            var invite = await Invites().FirstOrDefaultAsync(i => i.Code == Normalize(code));
            if (invite == null) return NotFound(new { reason = "not-found", message = "This invite link doesn't exist" });

            var user = await _context.Users.FirstAsync(u => u.Id == userId);
            var now = DateTime.UtcNow;
            var playerIds = invite.Players.Select(ip => ip.PlayerId).ToList();
            var result = new AcceptResultDto { Kind = invite.Kind, PlayerIds = playerIds };

            if (invite.Kind == FamilyInvite.PlayerKind)
            {
                var player = invite.Players.First().Player;
                // Opened twice by the same kid - already in.
                if (player.LinkedUserId == userId) return Ok(result);
                if (!invite.IsOpenAt(now))
                    return Conflict(new { reason = invite.UsedAt != null ? "used" : "expired", message = "This invite link was already used or has expired" });
                if (await _context.PlayerParents.AnyAsync(pp => pp.UserId == userId))
                    return Conflict(new { reason = "parent-account", message = "This is a parent account - the link is for the player's own account" });
                if (await _context.Players.AnyAsync(p => p.LinkedUserId == userId))
                    return Conflict(new { reason = "already-linked", message = "This account is already linked to a player" });
                if (player.LinkedUserId != null)
                    return Conflict(new { reason = "used", message = "This player already has an account" });

                player.LinkedUserId = userId;
                player.UpdatedAt = now;
                user.Role = "Player";
                user.UpdatedAt = now;
                invite.UsedAt = now;
            }
            else
            {
                if (!invite.IsOpenAt(now))
                    return Conflict(new { reason = "expired", message = "This invite link has expired" });
                if (user.Role == "Player")
                    return Conflict(new { reason = "player-account", message = "This is a player's account - the link is for a parent" });

                // Only kids the sender is still a parent of.
                var stillTheirs = await _context.PlayerParents
                    .Where(pp => pp.UserId == invite.CreatedByUserId && playerIds.Contains(pp.PlayerId))
                    .Select(pp => pp.PlayerId).ToListAsync();
                var already = await _context.PlayerParents
                    .Where(pp => pp.UserId == userId && stillTheirs.Contains(pp.PlayerId))
                    .Select(pp => pp.PlayerId).ToListAsync();
                foreach (var playerId in stillTheirs.Except(already))
                    _context.PlayerParents.Add(new PlayerParent { PlayerId = playerId, UserId = userId, CreatedAt = now });
                result.PlayerIds = stillTheirs;
            }

            if (userId != invite.CreatedByUserId && !invite.Joins.Any(j => j.UserId == userId))
                _context.FamilyInviteJoins.Add(new FamilyInviteJoin { InviteId = invite.Id, UserId = userId, JoinedAt = now });
            await _context.SaveChangesAsync();
            return Ok(result);
        }
    }
}
