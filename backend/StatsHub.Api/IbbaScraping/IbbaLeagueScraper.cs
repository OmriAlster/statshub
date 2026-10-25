using System.Globalization;
using System.Net;
using HtmlAgilityPack;
using StatsHub.Api.Services;
using StatsHub.Api.Leagues;

namespace StatsHub.Api.IbbaScraping;

public class IbbaLeagueScraper
{
    private readonly HttpClient _http;

    public IbbaLeagueScraper(HttpClient http) => _http = http;

    /// <summary>
    /// Loads the league page and parses the "רגילה" (regular season) standings table:
    /// מיקום | קבוצה | מש' | ניצ' | הפ' | טכני | קלעה | ספגה | הפרש | נק'
    /// </summary>
    public async Task<List<LeagueStanding>> GetStandingsAsync(string leagueUrl)
    {
        var html = await _http.GetStringAsync(leagueUrl);
        using var parsing = RequestTimings.Time("parse-standings");
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var standings = new List<LeagueStanding>();

        // The standings table is the one whose header row contains "מיקום" and "קבוצה"
        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null) return standings;

        HtmlNode? standingsTable = null;
        foreach (var table in tables)
        {
            var headerText = table.SelectSingleNode(".//tr")?.InnerText ?? "";
            if (headerText.Contains("מיקום") && headerText.Contains("קבוצה"))
            {
                standingsTable = table;
                break;
            }
        }

        if (standingsTable == null) return standings; // no table yet (e.g. before the season starts)

        // Columns found by their header (מש׳ / מש' alike), so a reordered table
        // still reads right and a renamed one stops the sync instead of
        // filling the table with another column's numbers.
        var headerCells = standingsTable.SelectSingleNode(".//tr")!.SelectNodes("./th|./td")?.ToList() ?? [];
        var column = headerCells
            .Select((cell, index) => (Name: Normalize(cell.InnerText), index))
            .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().index);
        var missing = StandingColumns.Where(name => !column.ContainsKey(name)).ToList();
        if (missing.Count > 0)
            throw new LeagueLayoutException($"standings table at {leagueUrl} is missing column(s) {string.Join(", ", missing)}");

        var bodyRows = standingsTable.SelectNodes(".//tr").Skip(1); // skip header
        foreach (var tr in bodyRows)
        {
            var cells = tr.SelectNodes("./td");
            if (cells == null || cells.Count < headerCells.Count) continue;
            string Cell(string name) => cells[column[name]].InnerText;

            var teamCell = cells[column["קבוצה"]];
            var teamLink = teamCell.SelectSingleNode(".//a");
            var rawTeamHref = teamLink?.GetAttributeValue("href", "") ?? "";
            var position = ParseInt(Cell("מיקום"));
            if (position <= 0 || string.IsNullOrEmpty(rawTeamHref))
                throw new LeagueLayoutException($"standings row \"{Normalize(tr.InnerText)}\" at {leagueUrl} has no position or team link");

            standings.Add(new LeagueStanding
            {
                Position = position,
                TeamName = WebUtility.HtmlDecode((teamLink?.InnerText ?? teamCell.InnerText).Trim()),
                TeamUrl = IbbaUrlHelper.Resolve(leagueUrl, rawTeamHref),
                GamesPlayed = ParseInt(Cell("מש")),
                Wins = ParseInt(Cell("ניצ")),
                Losses = ParseInt(Cell("הפ")),
                Technical = ParseInt(Cell("טכני")),
                PointsFor = ParseInt(Cell("קלעה")),
                PointsAgainst = ParseInt(Cell("ספגה")),
                Diff = ParseInt(Cell("הפרש"), allowNegative: true),
                LeaguePoints = ParseInt(Cell("נק")),
            });
        }

        return standings;
    }

    private static readonly string[] StandingColumns = ["מיקום", "קבוצה", "מש", "ניצ", "הפ", "טכני", "קלעה", "ספגה", "הפרש", "נק"];

    // Header text without spacing or the abbreviation mark (׳ or ').
    private static string Normalize(string text) =>
        System.Text.RegularExpressions.Regex.Replace(WebUtility.HtmlDecode(text), @"[\s'׳""]+", " ").Trim().Replace(" ", "");

    private static int ParseInt(string text, bool allowNegative = false)
    {
        text = text.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        return 0;
    }
}
