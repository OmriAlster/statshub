using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.DTOs;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SeasonsController : ControllerBase
    {
        private readonly ISeasonService _seasonService;
        private readonly ICurrentUserService _currentUser;

        public SeasonsController(ISeasonService seasonService, ICurrentUserService currentUser)
        {
            _seasonService = seasonService;
            _currentUser = currentUser;
        }

        [HttpGet]
        public async Task<ActionResult<List<SeasonDto>>> GetMySeasons()
        {
            var seasons = await _seasonService.GetSeasonsByUserAsync(_currentUser.UserId);
            return Ok(seasons);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<SeasonDto>> GetSeasonById(int id)
        {
            var season = await _seasonService.GetSeasonByIdAsync(id, _currentUser.UserId);
            if (season == null)
                return NotFound(new { message = "Season not found" });
            return Ok(season);
        }

        [HttpPost]
        public async Task<ActionResult<SeasonDto>> CreateSeason([FromBody] CreateSeasonDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var season = await _seasonService.CreateSeasonAsync(_currentUser.UserId, dto);
            return CreatedAtAction(nameof(GetSeasonById), new { id = season.Id }, season);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<SeasonDto>> UpdateSeason(int id, [FromBody] UpdateSeasonDto dto)
        {
            var season = await _seasonService.UpdateSeasonAsync(id, dto, _currentUser.UserId);
            if (season == null)
                return NotFound(new { message = "Season not found" });
            return Ok(season);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteSeason(int id)
        {
            var success = await _seasonService.DeleteSeasonAsync(id, _currentUser.UserId);
            if (!success)
                return NotFound(new { message = "Season not found" });
            return NoContent();
        }
    }
}
