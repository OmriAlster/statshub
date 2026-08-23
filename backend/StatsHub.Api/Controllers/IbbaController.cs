using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.DTOs;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    [ApiController]
    [Route("api")]
    [Authorize]
    public class IbbaController : ControllerBase
    {
        private readonly IIbbaService _ibbaService;
        private readonly IPlayerService _playerService;
        private readonly ICurrentUserService _currentUser;

        public IbbaController(IIbbaService ibbaService, IPlayerService playerService, ICurrentUserService currentUser)
        {
            _ibbaService = ibbaService;
            _playerService = playerService;
            _currentUser = currentUser;
        }

        [HttpGet("ibba/preview")]
        public async Task<ActionResult<IbbaPreviewDto>> Preview([FromQuery] string playerUrl)
        {
            if (string.IsNullOrWhiteSpace(playerUrl))
                return BadRequest(new { message = "playerUrl is required" });

            try
            {
                var preview = await _ibbaService.PreviewAsync(playerUrl);
                return Ok(preview);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception)
            {
                return BadRequest(new { message = "Could not read that IBBA page. Double-check the URL is a player profile page." });
            }
        }

        // Creates a brand-new player straight from an IBBA profile URL, instead of
        // requiring a manually-created player that gets connected to IBBA afterward.
        // Name comes from the scrape; jersey/position/DOB still need a human since
        // IBBA's own player page doesn't carry them.
        [HttpPost("ibba/players")]
        public async Task<ActionResult<CreatePlayerFromIbbaResultDto>> CreatePlayerFromIbba([FromBody] CreatePlayerFromIbbaDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.IbbaPlayerUrl))
                return BadRequest(new { message = "ibbaPlayerUrl is required" });

            IbbaPreviewDto preview;
            try
            {
                preview = await _ibbaService.PreviewAsync(dto.IbbaPlayerUrl);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception)
            {
                return BadRequest(new { message = "Could not read that IBBA page. Double-check the URL is a player profile page." });
            }

            if (preview.DateOfBirth == null)
                return BadRequest(new { message = "This IBBA profile doesn't list a birth date - add this player manually instead." });

            var nameParts = preview.PlayerName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var firstName = nameParts.Length > 1 ? string.Join(' ', nameParts[..^1]) : preview.PlayerName;
            var lastName = nameParts.Length > 1 ? nameParts[^1] : "";

            var player = await _playerService.CreatePlayerAsync(_currentUser.UserId, new CreatePlayerDto
            {
                FirstName = firstName,
                LastName = lastName,
                Position = "",
                DateOfBirth = preview.DateOfBirth.Value,
            });

            var ibba = await _ibbaService.LinkPlayerAsync(player.Id, dto.IbbaPlayerUrl, _currentUser.UserId);
            if (ibba != null)
                ibba = await _ibbaService.SyncPlayerAsync(player.Id, _currentUser.UserId);

            return Ok(new CreatePlayerFromIbbaResultDto { Player = player, Ibba = ibba });
        }

        [HttpPost("players/{playerId}/ibba/link")]
        public async Task<ActionResult<IbbaLinkStatusDto>> LinkPlayer(int playerId, [FromBody] LinkIbbaPlayerDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.IbbaPlayerUrl))
                return BadRequest(new { message = "ibbaPlayerUrl is required" });

            var status = await _ibbaService.LinkPlayerAsync(playerId, dto.IbbaPlayerUrl, _currentUser.UserId);
            if (status == null) return NotFound(new { message = "Player not found" });
            return Ok(status);
        }

        [HttpDelete("players/{playerId}/ibba/link")]
        public async Task<ActionResult> UnlinkPlayer(int playerId)
        {
            var success = await _ibbaService.UnlinkPlayerAsync(playerId, _currentUser.UserId);
            if (!success) return NotFound(new { message = "No IBBA link found for this player" });
            return NoContent();
        }

        [HttpGet("players/{playerId}/ibba")]
        public async Task<ActionResult<IbbaLinkStatusDto>> GetLinkStatus(int playerId)
        {
            var status = await _ibbaService.GetLinkStatusAsync(playerId, _currentUser.UserId);
            if (status == null) return NotFound(new { message = "No IBBA link found for this player" });
            return Ok(status);
        }

        [HttpPost("players/{playerId}/ibba/sync")]
        public async Task<ActionResult<IbbaLinkStatusDto>> SyncPlayer(int playerId)
        {
            var status = await _ibbaService.SyncPlayerAsync(playerId, _currentUser.UserId);
            if (status == null) return NotFound(new { message = "No IBBA link found for this player" });
            return Ok(status);
        }

        [HttpPut("ibba/team-links/{ibbaTeamLinkId}")]
        public async Task<ActionResult<IbbaLinkStatusDto>> LinkTeam(int ibbaTeamLinkId, [FromBody] LinkIbbaTeamDto dto)
        {
            var status = await _ibbaService.LinkTeamAsync(ibbaTeamLinkId, dto.TeamId, _currentUser.UserId);
            if (status == null) return NotFound(new { message = "IBBA team link or team not found" });
            return Ok(status);
        }

        [HttpGet("ibba/standings")]
        public async Task<ActionResult<List<IbbaStandingDto>>> GetStandings([FromQuery] string leagueUrl)
        {
            if (string.IsNullOrWhiteSpace(leagueUrl))
                return BadRequest(new { message = "leagueUrl is required" });

            var standings = await _ibbaService.GetStandingsAsync(leagueUrl);
            return Ok(standings);
        }
    }
}
