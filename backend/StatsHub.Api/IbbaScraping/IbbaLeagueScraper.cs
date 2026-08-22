using System.Globalization;
using HtmlAgilityPack;

namespace StatsHub.Api.IbbaScraping;

public class IbbaLeagueScraper
{
    private readonly HttpClient _http;

    public IbbaLeagueScraper(HttpClient http) => _http = http;

    /// <summary>
    /// Loads the league page and parses the "רגילה" (regular season) standings table:
    /// מיקום | קבוצה | מש' | ניצ' | הפ' | טכני | קלעה | ספגה | הפרש | נק'
    /// </summary>
    public async Task<List<IbbaStandingRow>> GetStandingsAsync(string leagueUrl)
    {
        var html = await _http.GetStringAsync(leagueUrl);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var standings = new List<IbbaStandingRow>();

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

        if (standingsTable == null) return standings;

        var bodyRows = standingsTable.SelectNodes(".//tr").Skip(1); // skip header
        foreach (var tr in bodyRows)
        {
            var cells = tr.SelectNodes("./td");
            if (cells == null || cells.Count < 10) continue;

            var teamLink = cells[1].SelectSingleNode(".//a");
            var rawTeamHref = teamLink?.GetAttributeValue("href", "") ?? "";

            standings.Add(new IbbaStandingRow
            {
                Position = ParseInt(cells[0].InnerText),
                TeamName = (teamLink?.InnerText ?? cells[1].InnerText).Trim(),
                TeamUrl = string.IsNullOrEmpty(rawTeamHref) ? "" : IbbaUrlHelper.Resolve(leagueUrl, rawTeamHref),
                GamesPlayed = ParseInt(cells[2].InnerText),
                Wins = ParseInt(cells[3].InnerText),
                Losses = ParseInt(cells[4].InnerText),
                Technical = ParseInt(cells[5].InnerText),
                PointsFor = ParseInt(cells[6].InnerText),
                PointsAgainst = ParseInt(cells[7].InnerText),
                Diff = ParseInt(cells[8].InnerText, allowNegative: true),
                LeaguePoints = ParseInt(cells[9].InnerText),
            });
        }

        return standings;
    }

    private static int ParseInt(string text, bool allowNegative = false)
    {
        text = text.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        return 0;
    }
}
