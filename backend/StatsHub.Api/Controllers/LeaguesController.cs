using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.Leagues;

namespace StatsHub.Api.Controllers
{
    // The external leagues players can be linked to - their names and marks
    // for the website (e.g. next to a synced game, keyed by GameDto.League).
    [ApiController]
    [Route("api/leagues")]
    [AllowAnonymous] // the public share view shows league marks too
    public class LeaguesController : ControllerBase
    {
        private readonly LeagueProviders _leagues;
        public LeaguesController(LeagueProviders leagues) => _leagues = leagues;

        [HttpGet]
        public IEnumerable<LeagueDto> Get() => _leagues.All.Select(l => new LeagueDto(l.Key, l.Name, l.LogoUrl, l.PlayerUrlExample));

        public record LeagueDto(string Key, string Name, string LogoUrl, string PlayerUrlExample);
    }
}
