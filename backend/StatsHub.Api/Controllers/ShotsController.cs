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
