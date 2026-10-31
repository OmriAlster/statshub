using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.Leagues;
using StatsHub.Api.Services;

namespace StatsHub.Api.Controllers
{
    // The players' games in the phone's calendar - see CalendarFeed.
    [ApiController]
    [Route("api/calendar")]
    public class CalendarController : ControllerBase
    {
        private readonly IPlayerService _playerService;
        private readonly IGameService _gameService;
        private readonly ICurrentUserService _currentUser;
        private readonly IConfiguration _configuration;

        public CalendarController(IPlayerService playerService, IGameService gameService, ICurrentUserService currentUser, IConfiguration configuration)
        {
            _playerService = playerService;
            _gameService = gameService;
            _currentUser = currentUser;
            _configuration = configuration;
        }

        private string Secret => _configuration["Jwt:Key"] ?? "dev-only-calendar-secret";

        // This account's calendar address, for "Add to calendar".
        [Authorize]
        [HttpGet("link")]
        public ActionResult<object> GetLink()
        {
            var url = $"{Request.Scheme}://{Request.Host}/api/calendar/{CalendarFeed.TokenFor(_currentUser.UserId, Secret)}.ics";
            return Ok(new { url, webcalUrl = "webcal://" + url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..] });
        }

        // Read by the calendar app itself (no sign-in) - this season's games of
        // every player on the account.
        [AllowAnonymous]
        [HttpGet("{token}.ics")]
        public async Task<IActionResult> GetFeed(string token)
        {
            if (CalendarFeed.UserFrom(token, Secret) is not Guid userId) return NotFound();

            var seasonStart = LeagueSeason.StartOf(DateTime.UtcNow);
            var byGame = new Dictionary<Guid, CalendarFeed.Entry>();
            var players = await _playerService.GetPlayersOwnedByAsync(userId);
            if (await _playerService.GetLinkedPlayerAsync(userId) is { } own && players.All(p => p.Id != own.Id)) players.Add(own); // a player's own login
            foreach (var player in players)
            {
                foreach (var game in await _gameService.GetGamesByPlayerAsync(player.Id, userId))
                {
                    if (game.GameDate < seasonStart) continue;
                    if (byGame.TryGetValue(game.Id, out var entry)) entry.PlayerNames.Add(player.FirstName);
                    else byGame[game.Id] = new CalendarFeed.Entry(game, [player.FirstName]);
                }
            }

            var appUrl = (_configuration["FrontendUrl"] ?? "http://localhost:5173").Split(',')[0].Trim().TrimEnd('/');
            var ics = CalendarFeed.Build(byGame.Values, appUrl, DateTime.UtcNow);
            return File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8");
        }
    }
}
