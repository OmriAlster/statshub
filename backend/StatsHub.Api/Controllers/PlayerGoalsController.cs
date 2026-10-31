using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.Leagues;
using StatsHub.Api.Models;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    // The player's goals for this season - set by the parents, seen by the
    // player too; last season's simply stop showing.
    [ApiController]
    [Route("api/players/{playerId}/goals")]
    [Authorize]
    public class PlayerGoalsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ICurrentUserService _currentUser;

        public PlayerGoalsController(AppDbContext context, ICurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public class GoalDto
        {
            public string Stat { get; set; } = string.Empty;
            public double Target { get; set; }
        }

        [HttpGet]
        public async Task<ActionResult<List<GoalDto>>> Get(Guid playerId)
        {
            if (!await _context.CanReadPlayerAsync(playerId, _currentUser.UserId)) return NotFound();
            var seasonStart = LeagueSeason.StartOf(DateTime.UtcNow);
            return await _context.PlayerGoals
                .Where(g => g.PlayerId == playerId && g.CreatedAt >= seasonStart)
                .OrderBy(g => g.CreatedAt)
                .Select(g => new GoalDto { Stat = g.Stat, Target = g.Target })
                .ToListAsync();
        }

        // The whole list at once - what's left out is removed.
        [HttpPut]
        public async Task<ActionResult<List<GoalDto>>> Set(Guid playerId, [FromBody] List<GoalDto> goals)
        {
            if (!await _context.CanWritePlayerAsync(playerId, _currentUser.UserId)) return NotFound();
            if (goals.Count > PlayerGoal.Stats.Count || goals.Select(g => g.Stat).Distinct().Count() != goals.Count)
                return BadRequest(new { message = "One goal per stat" });
            foreach (var goal in goals)
            {
                if (!PlayerGoal.Stats.TryGetValue(goal.Stat, out var max)) return BadRequest(new { message = $"Unknown stat: {goal.Stat}" });
                if (goal.Target <= 0 || goal.Target > max) return BadRequest(new { message = $"{goal.Stat} goal must be between 0 and {max}" });
            }

            var existing = await _context.PlayerGoals.Where(g => g.PlayerId == playerId).ToListAsync();
            var seasonStart = LeagueSeason.StartOf(DateTime.UtcNow);
            foreach (var old in existing.Where(o => o.CreatedAt < seasonStart || goals.All(g => g.Stat != o.Stat)))
                _context.PlayerGoals.Remove(old);
            foreach (var goal in goals)
            {
                var current = existing.FirstOrDefault(o => o.Stat == goal.Stat && o.CreatedAt >= seasonStart);
                if (current != null) current.Target = goal.Target;
                else _context.PlayerGoals.Add(new PlayerGoal { PlayerId = playerId, Stat = goal.Stat, Target = goal.Target });
            }
            await _context.SaveChangesAsync();
            return await Get(playerId);
        }
    }
}
