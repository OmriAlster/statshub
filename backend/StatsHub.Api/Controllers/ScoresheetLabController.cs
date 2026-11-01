using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatsHub.Api.Scoresheets;

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
            public string Team { get; set; } = string.Empty;
            public List<int> Numbers { get; set; } = new();
            public List<LabGameDto> Games { get; set; } = new();
            public int UpcomingGames { get; set; }
        }

        // Letters and digits only - "בית"ר" / "ביתר", "גזר/נען" / "גזר נען" compare equal.
        private static string Norm(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var sb = new StringBuilder();
            foreach (var ch in s)
                if (char.IsLetterOrDigit(ch) && CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        // Swish cuts team names at 20 characters ("הפועל נצר סרני אריק/"), so a long name
        // matches on its start.
        private static bool SameTeam(string swishName, string ibbaName)
        {
            string a = Norm(swishName), b = Norm(ibbaName);
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            return Math.Min(a.Length, b.Length) >= 10 && (a.StartsWith(b) || b.StartsWith(a));
        }

        // The sheet only has a last name and an initial; the IBBA name is "first ... last", where
        // either part can be more than one word - every split is tried, and one player must fit.
        private static (ScoresheetMatchStatus Status, ScoresheetPlayer? Player, List<ScoresheetPlayer> Candidates) FindPlayer(
            string playerName, IReadOnlyList<ScoresheetPlayer> roster)
        {
            var words = playerName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var found = new List<ScoresheetPlayer>();
            for (int k = 1; k < words.Length; k++)
            {
                var match = ScoresheetNameMatcher.Match(string.Join(' ', words[..k]), string.Join(' ', words[k..]), roster);
                found.AddRange(match.Candidates);
            }
            found = found.DistinctBy(p => p.Code).ToList();
            return found.Count switch
            {
                0 => (ScoresheetMatchStatus.NotFound, null, found),
                1 => (ScoresheetMatchStatus.Matched, found[0], found),
                _ => (ScoresheetMatchStatus.Ambiguous, null, found),
            };
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

            // The team's league in Swish: by the IBBA league's name when known, else every league.
            var competitions = await _swish.CompetitionsAsync(ct);
            // A known league that Swish doesn't have has no digital scoresheets - searching every
            // league would find the same club's other teams (a women's cup for a boys' team).
            var byName = competitions.Where(c => Norm(c.Name) == Norm(leagueName)).ToList();
            if (!string.IsNullOrWhiteSpace(leagueName) && byName.Count == 0)
                return NotFound(new { message = $"\"{leagueName}\" has no digital scoresheets on Swish." });
            var searchIn = byName.Count > 0 ? byName : competitions;

            foreach (var competition in searchIn)
            {
                foreach (var group in competition.Groups ?? new())
                {
                    var calendar = await _swish.CalendarAsync(group.Id, ct);
                    var games = (calendar.Rounds ?? new()).SelectMany(r => r.Matches ?? new())
                        .Where(g => SameTeam(g.HomeTeam.Name, teamName) || SameTeam(g.AwayTeam.Name, teamName))
                        .ToList();
                    if (games.Count == 0) continue;

                    var result = new LabResultDto
                    {
                        League = competition.Name + (competition.Groups!.Count > 1 ? $" · {group.Name}" : ""),
                        Team = SameTeam(games[0].HomeTeam.Name, teamName) ? games[0].HomeTeam.Name : games[0].AwayTeam.Name,
                        UpcomingGames = games.Count(g => g.Status != "CLOSED"),
                    };

                    // Newest first; for the jersey number, stop at the first sheet that has the player.
                    foreach (var game in games.Where(g => g.Status == "CLOSED").OrderByDescending(g => g.Date))
                    {
                        bool home = SameTeam(game.HomeTeam.Name, teamName);
                        var mine = home ? game.HomeTeam : game.AwayTeam;
                        var theirs = home ? game.AwayTeam : game.HomeTeam;
                        var totals = (game.Score?.Totals ?? new()).ToDictionary(t => t.TeamId, t => t.Total);
                        var row = new LabGameDto
                        {
                            Date = game.Date,
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
                        if (status == ScoresheetMatchStatus.NotFound) { row.Status = "not-on-sheet"; }
                        else if (status == ScoresheetMatchStatus.Ambiguous)
                        {
                            row.Status = "ambiguous";
                            row.Note = string.Join(", ", candidates.Select(c => $"#{c.Number} {c.LastName} {c.FirstInitial}."));
                        }
                        else
                        {
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
                    }
                    result.Numbers = result.Games.Where(g => g.Number is not null).Select(g => g.Number!.Value).Distinct().ToList();
                    return result;
                }
            }
            return NotFound(new { message = $"\"{teamName}\" wasn't found in Swish - its league may have no digital scoresheets." });
        }
    }
}
