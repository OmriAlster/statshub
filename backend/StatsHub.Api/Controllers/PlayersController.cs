using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.DTOs;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PlayersController : ControllerBase
    {
        private readonly IPlayerService _playerService;
        private readonly ICurrentUserService _currentUser;

        public PlayersController(IPlayerService playerService, ICurrentUserService currentUser)
        {
            _playerService = playerService;
            _currentUser = currentUser;
        }

        [HttpGet]
        public async Task<ActionResult<List<PlayerDto>>> GetMyPlayers()
        {
            var players = await _playerService.GetPlayersOwnedByAsync(_currentUser.UserId);
            return Ok(players);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<PlayerDto>> GetPlayerById(int id)
        {
            var player = await _playerService.GetPlayerByIdAsync(id, _currentUser.UserId);
            if (player == null)
                return NotFound(new { message = "Player not found" });
            return Ok(player);
        }

        [HttpPost]
        public async Task<ActionResult<PlayerDto>> CreatePlayer([FromBody] CreatePlayerDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var player = await _playerService.CreatePlayerAsync(_currentUser.UserId, dto);
            return CreatedAtAction(nameof(GetPlayerById), new { id = player.Id }, player);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<PlayerDto>> UpdatePlayer(int id, [FromBody] UpdatePlayerDto dto)
        {
            var player = await _playerService.UpdatePlayerAsync(id, dto, _currentUser.UserId);
            if (player == null)
                return NotFound(new { message = "Player not found" });
            return Ok(player);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeletePlayer(int id)
        {
            var success = await _playerService.DeletePlayerAsync(id, _currentUser.UserId);
            if (!success)
                return NotFound(new { message = "Player not found" });
            return NoContent();
        }

        // Generates a short-lived code the parent can send to their player so the
        // player can sign in with their own Google account and see their own stats.
        [HttpPost("{id}/invite")]
        public async Task<ActionResult<PlayerInviteDto>> CreatePlayerInvite(int id, [FromBody] CreatePlayerInviteDto? dto)
        {
            var invite = await _playerService.CreatePlayerInviteAsync(id, _currentUser.UserId, dto?.Password);
            if (invite == null)
                return NotFound(new { message = "Player not found" });
            return Ok(invite);
        }

        [HttpPost("claim-invite")]
        public async Task<ActionResult<PlayerDto>> ClaimInvite([FromBody] ClaimInviteDto dto)
        {
            var player = await _playerService.ClaimPlayerInviteAsync(dto.InviteCode.Trim().ToUpperInvariant(), _currentUser.UserId, dto.Password);
            if (player == null)
                return BadRequest(new { message = "Invite code or password is invalid or expired" });
            return Ok(player);
        }

        // Generates a short-lived code a second parent/guardian can use to get
        // full access to the same player (e.g. mom invites dad).
        [HttpPost("{id}/parent-invite")]
        public async Task<ActionResult<ParentInviteDto>> CreateParentInvite(int id, [FromBody] CreateParentInviteDto? dto)
        {
            var invite = await _playerService.CreateParentInviteAsync(id, _currentUser.UserId, dto?.Password);
            if (invite == null)
                return NotFound(new { message = "Player not found" });
            return Ok(invite);
        }

        [HttpPost("claim-parent-invite")]
        public async Task<ActionResult<PlayerDto>> ClaimParentInvite([FromBody] ClaimParentInviteDto dto)
        {
            var player = await _playerService.ClaimParentInviteAsync(dto.InviteCode.Trim().ToUpperInvariant(), _currentUser.UserId, dto.Password);
            if (player == null)
                return BadRequest(new { message = "Invite code or password is invalid or expired" });
            return Ok(player);
        }
    }
}
