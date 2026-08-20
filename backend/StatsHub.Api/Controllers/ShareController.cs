using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.DTOs;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShareController : ControllerBase
    {
        private readonly IShareService _shareService;
        private readonly ICurrentUserService _currentUser;

        public ShareController(IShareService shareService, ICurrentUserService currentUser)
        {
            _shareService = shareService;
            _currentUser = currentUser;
        }

        [Authorize]
        [HttpPost]
        public async Task<ActionResult<ShareLinkDto>> CreateShareLink([FromBody] CreateShareLinkDto dto)
        {
            var link = await _shareService.CreateShareLinkAsync(dto, _currentUser.UserId);
            if (link == null)
                return NotFound(new { message = "Player or game not found" });
            return Ok(link);
        }

        // Public, unauthenticated: anyone with the link/token can view read-only stats.
        [AllowAnonymous]
        [HttpGet("{token}")]
        public async Task<ActionResult<SharedPlayerDto>> GetShared(string token)
        {
            var shared = await _shareService.GetByTokenAsync(token);
            if (shared == null)
                return NotFound(new { message = "This share link is invalid or has expired" });
            return Ok(shared);
        }
    }
}
