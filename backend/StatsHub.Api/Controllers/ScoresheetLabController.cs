using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.Scoresheets;
using static StatsHub.Api.Scoresheets.SwishMatching;

namespace StatsHub.Api.Controllers
{
    // A trial page (Development only): an IBBA player's team, found in Swish by name, and
    // the player read off each of the team's game scoresheets - every game, or only the
    // jersey number from the latest sheet.
    [ApiController]
    [Route("api/scoresheet-lab")]
    [Authorize]
    public class ScoresheetLabController : ControllerBase
    {
        private readonly SwishClient _swish;
        private readonly IWebHostEnvironment _env;

        public ScoresheetLabController(SwishClient swish, IWebHostEnvironment env)
        {
            _swish = swish;
            _env = env;
        }

        public class LabGameDto
        {
            public DateTime Date { get; set; }
            // The league or cup the game was in.
            public string Competition { get; set; } = string.Empty;
            public bool Cup { get; set; }
            public string Opponent { get; set; } = string.Empty;
            public bool Home { get; set; }
            public int? TeamScore { get; set; }
            public int? OpponentScore { get; set; }
            // found, not-on-sheet, ambiguous, no-sheet, invalid-sheet
            public string Status { get; set; } = string.Empty;
            public bool SheetValid { get; set; }
            public string? Note { get; set; }
            public int? Number { get; set; }
            public string? Code { get; set; }
            public string? NameOnSheet { get; set; }
            public bool Played { get; set; }
            public bool Starter { get; set; }
            public bool Captain { get; set; }
            public int Points { get; set; }
            public int FreeThrows { get; set; }
            public int TwoPointers { get; set; }
            public int ThreePointers { get; set; }
            public int Fouls { get; set; }
        }

        public class LabResultDto
        {
            public string League { get; set; } = string.Empty;
            // The age group's cups the team was found in.
            public List<string> Cups { get; set; } = new();
            public string Team { get; set; } = string.Empty;
            public List<int> Numbers { get; set; } = new();
            public List<LabGameDto> Games { get; set; } = new();
            public int UpcomingGames { get; set; }
        }

        [HttpGet]
        public async Task<ActionResult<LabResultDto>> Get(
            [FromQuery] string playerName, [FromQuery] string teamName, [FromQuery] string? leagueName,
            [FromQuery] string mode = "games", CancellationToken ct = default)
        {
            if (!_env.IsDevelopment()) return NotFound();
            if (!_swish.IsConfigured)
                return StatusCode(503, new { message = "Swish isn't set up here - set SWISH_API_KEY and restart the server." });
            if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(teamName))
                return BadRequest(new { message = "playerName and teamName are required" });

            var competitions = await _swish.CompetitionsAsync(ct);

            // The team's league: by the IBBA league's name when known, else the first league
            // that has a team of that name. A known league Swish doesn't have has no digital
            // scoresheets - searching every league would find the same club's other teams.
            var leagues = competitions.Where(c => !IsCup(c)).ToList();
            var named = leagues.Where(c => Norm(c.Name) == Norm(leagueName)).ToList();
            if (!string.IsNullOrWhiteSpace(leagueName) && named.Count == 0)
                return NotFound(new { message = $"\"{leagueName}\" has no digital scoresheets on Swish." });

            SwishCompetition? league = null;
            var found = new List<(SwishCompetition Competition, SwishGame Game)>();
            foreach (var competition in named.Count > 0 ? named : leagues)
            {
                var games = await TeamGamesAsync(competition, teamName, ct);
                if (games.Count == 0) continue;
                league = competition;
                found.AddRange(games.Select(g => (competition, g)));
                break;
            }
            if (league is null)
                return NotFound(new { message = $"\"{teamName}\" wasn't found in Swish - its league may have no digital scoresheets." });

            // ...and the cups of the league's age group.
            var cups = new List<string>();
            foreach (var cup in competitions.Where(c => IsCup(c) && CupFor(c, AgeGroupOf(league.Name))))
            {
                var games = await TeamGamesAsync(cup, teamName, ct);
                if (games.Count == 0) continue;
                cups.Add(cup.Name);
                found.AddRange(games.Select(g => (cup, g)));
            }

            var first = found[0].Game;
            var result = new LabResultDto
            {
                League = league.Name,
                Cups = cups,
                Team = SameTeam(first.HomeTeam.Name, teamName) ? first.HomeTeam.Name : first.AwayTeam.Name,
                UpcomingGames = found.Count(f => f.Game.Status != "CLOSED"),
            };

            // Newest first; for the jersey number, stop at the first sheet that has the player.
            foreach (var (competition, game) in found.Where(f => f.Game.Status == "CLOSED").OrderByDescending(f => f.Game.Date))
            {
                bool home = SameTeam(game.HomeTeam.Name, teamName);
                var mine = home ? game.HomeTeam : game.AwayTeam;
                var theirs = home ? game.AwayTeam : game.HomeTeam;
                var totals = (game.Score?.Totals ?? new()).ToDictionary(t => t.TeamId, t => t.Total);
                var row = new LabGameDto
                {
                    Date = game.Date,
                    Competition = competition.Name,
                    Cup = IsCup(competition),
                    Opponent = theirs.Name,
                    Home = home,
                    TeamScore = totals.TryGetValue(mine.Id, out var ts) ? ts : null,
                    OpponentScore = totals.TryGetValue(theirs.Id, out var os) ? os : null,
                };
                result.Games.Add(row);

                var sheet = await _swish.ScoresheetAsync(game.Id, ct);
                if (sheet is null) { row.Status = "no-sheet"; continue; }
                row.SheetValid = sheet.IsValid;
                // The sheet's home team is team A; fall back to the name in case it differs.
                var team = SameTeam(sheet.Home.Name, teamName) ? sheet.Home
                    : SameTeam(sheet.Away.Name, teamName) ? sheet.Away
                    : home ? sheet.Home : sheet.Away;
                var (status, player, candidates) = FindPlayer(playerName, team.Players);
                if (status == ScoresheetMatchStatus.NotFound) { row.Status = "not-on-sheet"; continue; }
                if (status == ScoresheetMatchStatus.Ambiguous)
                {
                    row.Status = "ambiguous";
                    row.Note = string.Join(", ", candidates.Select(c => $"#{c.Number} {c.LastName} {c.FirstInitial}."));
                    continue;
                }
                row.Status = sheet.IsValid ? "found" : "invalid-sheet";
                row.Number = player!.Number;
                row.Code = player.Code;
                row.NameOnSheet = $"{player.LastName}, {player.FirstInitial}.";
                row.Played = player.Played;
                row.Starter = player.Starter;
                row.Captain = player.Captain;
                row.Points = player.Points;
                row.FreeThrows = player.FreeThrows;
                row.TwoPointers = player.TwoPointers;
                row.ThreePointers = player.ThreePointers;
                row.Fouls = player.Fouls;
                if (!sheet.IsValid) row.Note = "The sheet doesn't add up - not used: " + string.Join("; ", sheet.Errors.Take(2));
                if (mode == "jersey") break;
            }
            result.Numbers = result.Games.Where(g => g.Number is not null).Select(g => g.Number!.Value).Distinct().ToList();
            return result;
        }

        // A team's games in one league or cup (every group of it).
        private async Task<List<SwishGame>> TeamGamesAsync(SwishCompetition competition, string teamName, CancellationToken ct)
        {
            var games = new List<SwishGame>();
            foreach (var group in competition.Groups ?? new())
            {
                var calendar = await _swish.CalendarAsync(group.Id, ct);
                games.AddRange((calendar.Rounds ?? new()).SelectMany(r => r.Matches ?? new())
                    .Where(g => SameTeam(g.HomeTeam.Name, teamName) || SameTeam(g.AwayTeam.Name, teamName)));
            }
            return games;
        }
    }
}
