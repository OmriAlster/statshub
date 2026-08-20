using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.DTOs;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class GamesController : ControllerBase
    {
        private readonly IGameService _gameService;
        private readonly ICurrentUserService _currentUser;

        public GamesController(IGameService gameService, ICurrentUserService currentUser)
        {
            _gameService = gameService;
            _currentUser = currentUser;
        }

        [HttpGet("team/{teamId}")]
        public async Task<ActionResult<List<GameDto>>> GetGamesByTeam(int teamId)
        {
            var games = await _gameService.GetGamesByTeamAsync(teamId, _currentUser.UserId);
            return Ok(games);
        }

        [HttpGet("player/{playerId}")]
        public async Task<ActionResult<List<GameDto>>> GetGamesByPlayer(int playerId)
        {
            var games = await _gameService.GetGamesByPlayerAsync(playerId, _currentUser.UserId);
            return Ok(games);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<GameDto>> GetGameById(int id)
        {
            var game = await _gameService.GetGameByIdAsync(id, _currentUser.UserId);
            if (game == null)
                return NotFound(new { message = "Game not found" });
            return Ok(game);
        }

        [HttpPost]
        public async Task<ActionResult<GameDto>> CreateGame([FromBody] CreateGameDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var game = await _gameService.CreateGameAsync(dto, _currentUser.UserId);
                return CreatedAtAction(nameof(GetGameById), new { id = game.Id }, game);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<GameDto>> UpdateGame(int id, [FromBody] UpdateGameDto dto)
        {
            var game = await _gameService.UpdateGameAsync(id, dto, _currentUser.UserId);
            if (game == null)
                return NotFound(new { message = "Game not found" });
            return Ok(game);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteGame(int id)
        {
            var success = await _gameService.DeleteGameAsync(id, _currentUser.UserId);
            if (!success)
                return NotFound(new { message = "Game not found" });
            return NoContent();
        }
    }
}
