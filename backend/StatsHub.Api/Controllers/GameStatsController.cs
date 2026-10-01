using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.DTOs;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class GameStatsController : ControllerBase
    {
        private readonly IGameStatsService _gameStatsService;
        private readonly ICurrentUserService _currentUser;

        public GameStatsController(IGameStatsService gameStatsService, ICurrentUserService currentUser)
        {
            _gameStatsService = gameStatsService;
            _currentUser = currentUser;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<GameStatsDto>> GetGameStatsById(Guid id)
        {
            var stats = await _gameStatsService.GetGameStatsByIdAsync(id, _currentUser.UserId);
            if (stats == null)
                return NotFound(new { message = "Game stats not found" });
            return Ok(stats);
        }

        [HttpPost]
        public async Task<ActionResult<GameStatsDto>> CreateGameStats([FromBody] CreateGameStatsDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var stats = await _gameStatsService.CreateGameStatsAsync(dto, _currentUser.UserId);
                return CreatedAtAction(nameof(GetGameStatsById), new { id = stats.Id }, stats);
            }
            catch (UnauthorizedAccessException ex)
            {
                // Not Forbid(ex.Message) - that treats the message as the name of an
                // authentication scheme, which doesn't exist, and turns a
                // plain "not yours" into a 500.
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<GameStatsDto>> UpdateGameStats(Guid id, [FromBody] UpdateGameStatsDto dto)
        {
            var stats = await _gameStatsService.UpdateGameStatsAsync(id, dto, _currentUser.UserId);
            if (stats == null)
                return NotFound(new { message = "Game stats not found" });
            return Ok(stats);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteGameStats(Guid id)
        {
            var success = await _gameStatsService.DeleteGameStatsAsync(id, _currentUser.UserId);
            if (!success)
                return NotFound(new { message = "Game stats not found" });
            return NoContent();
        }

        [HttpGet("player/{playerId}/team/{teamId}")]
        public async Task<ActionResult<PlayerTeamStatsDto>> GetTeamStatsForPlayer(Guid playerId, Guid teamId)
        {
            var stats = await _gameStatsService.GetTeamStatsForPlayerAsync(playerId, teamId, _currentUser.UserId);
            if (stats == null)
                return NotFound(new { message = "Player/team stats not found" });
            return Ok(stats);
        }

        // One entry per team the player is rostered on, each with its own stat line.
        [HttpGet("player/{playerId}")]
        public async Task<ActionResult<List<PlayerTeamStatsDto>>> GetStatsByPlayer(Guid playerId)
        {
            var stats = await _gameStatsService.GetStatsByPlayerAsync(playerId, _currentUser.UserId);
            return Ok(stats);
        }
    }
}
