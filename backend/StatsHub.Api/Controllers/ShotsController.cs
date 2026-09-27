using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.DTOs;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ShotsController : ControllerBase
    {
        private readonly IShotService _shotService;
        private readonly ICurrentUserService _currentUser;

        public ShotsController(IShotService shotService, ICurrentUserService currentUser)
        {
            _shotService = shotService;
            _currentUser = currentUser;
        }

        [HttpPost]
        public async Task<ActionResult<ShotDto>> CreateShot([FromBody] CreateShotDto dto)
        {
            // A shot is a 2 or a 3 (anything else was silently counted as a
            // 2-pointer), in regulation quarters 1-4 (all the app records),
            // at a spot on the court (positions are 0-1 of the court's width
            // and height).
            if (dto.Value is not (2 or 3))
                return BadRequest(new { message = "A shot is worth 2 or 3 points" });
            if (dto.Quarter is < 1 or > 4)
                return BadRequest(new { message = "Quarter must be 1-4" });
            if (dto.X is < 0 or > 1 || dto.Y is < 0 or > 1)
                return BadRequest(new { message = "Shot position is off the court" });

            var shot = await _shotService.CreateShotAsync(dto, _currentUser.UserId);
            if (shot == null)
                return NotFound(new { message = "Game stats not found or not owned by user" });
            return Ok(shot);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteShot(int id)
        {
            var success = await _shotService.DeleteShotAsync(id, _currentUser.UserId);
            if (!success)
                return NotFound(new { message = "Shot not found" });
            return NoContent();
        }

        [HttpGet("gamestats/{gameStatsId}")]
        public async Task<ActionResult<List<ShotDto>>> GetShotsByGameStats(int gameStatsId)
        {
            var shots = await _shotService.GetShotsByGameStatsAsync(gameStatsId, _currentUser.UserId);
            return Ok(shots);
        }

        [HttpGet("player/{playerId}/team/{teamId}")]
        public async Task<ActionResult<List<ShotDto>>> GetShotsByPlayerAndTeam(int playerId, int teamId)
        {
            var shots = await _shotService.GetShotsByPlayerAndTeamAsync(playerId, teamId, _currentUser.UserId);
            return Ok(shots);
        }
    }
}
