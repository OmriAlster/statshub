using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.DTOs;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TeamsController : ControllerBase
    {
        private readonly ITeamService _teamService;
        private readonly ICurrentUserService _currentUser;

        public TeamsController(ITeamService teamService, ICurrentUserService currentUser)
        {
            _teamService = teamService;
            _currentUser = currentUser;
        }

        [HttpGet]
        public async Task<ActionResult<List<TeamDto>>> GetMyTeams()
        {
            var teams = await _teamService.GetMyTeamsAsync(_currentUser.UserId);
            return Ok(teams);
        }

        [HttpPost]
        public async Task<ActionResult<TeamDto>> CreateTeam([FromBody] CreateTeamDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { message = "Team name is required" });

            var team = await _teamService.CreateTeamAsync(_currentUser.UserId, dto);
            return Ok(team);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteTeam(int id)
        {
            var success = await _teamService.DeleteTeamAsync(id, _currentUser.UserId);
            if (!success)
                return NotFound(new { message = "Team not found" });
            return NoContent();
        }

        [HttpPost("{teamId}/players/{playerId}")]
        public async Task<ActionResult> AddPlayerToTeam(int teamId, int playerId, [FromBody] AddPlayerToTeamDto? dto)
        {
            var success = await _teamService.AddPlayerToTeamAsync(teamId, playerId, _currentUser.UserId, dto?.JerseyNumber);
            if (!success)
                return NotFound(new { message = "Team or player not found" });
            return NoContent();
        }

        [HttpDelete("{teamId}/players/{playerId}")]
        public async Task<ActionResult> RemovePlayerFromTeam(int teamId, int playerId)
        {
            var success = await _teamService.RemovePlayerFromTeamAsync(teamId, playerId, _currentUser.UserId);
            if (!success)
                return NotFound(new { message = "Team membership not found" });
            return NoContent();
        }

        [HttpPut("{teamId}/players/{playerId}")]
        public async Task<ActionResult> UpdatePlayerTeamJersey(int teamId, int playerId, [FromBody] UpdatePlayerTeamDto dto)
        {
            var success = await _teamService.UpdatePlayerTeamJerseyAsync(teamId, playerId, dto.JerseyNumber, _currentUser.UserId);
            if (!success)
                return NotFound(new { message = "Team membership not found" });
            return NoContent();
        }
    }
}
