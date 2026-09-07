using System.Net;
using ClosedXML.Excel;
using HtmlAgilityPack;

namespace StatsHub.Api.IbbaScraping;

public class IbbaTeamScraper
{
    private readonly HttpClient _http;

    public IbbaTeamScraper(HttpClient http) => _http = http;

    /// <summary>
    /// Fetches a team's own page once. Name, crest, the Excel export link, and
    /// the league directory are all on this single page - previously each had
    /// its own method that fetched and re-parsed the exact same URL from
    /// scratch, meaning one team cost 4 full page loads instead of 1. Callers
    /// that need more than one of those should load the page once here and
    /// pass the resulting document to the parse-only methods below.
    /// </summary>
    public async Task<HtmlDocument> LoadTeamPageAsync(string teamPageUrl)
    {
        var html = await _http.GetStringAsync(teamPageUrl);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }

    /// <summary>
    /// Finds the "יצוא לאקסל" (Export to Excel) link on an already-loaded team page.
    /// IMPORTANT: we never construct this URL ourselves. The team page slug id
    /// (from /team/10550-.../) and the export's internal team_id (e.g. 746561)
    /// are two DIFFERENT ids on this site - the export link only works with the
    /// real one, which is embedded in the href itself. We just read it off the page.
    /// </summary>
    public string? FindExcelExportUrl(HtmlDocument doc, string teamPageUrl)
    {
        var exportNode = doc.DocumentNode
            .SelectNodes("//a[contains(@href,'feed=xlsx')]")
            ?.FirstOrDefault();

        var rawHref = exportNode?.GetAttributeValue("href", null);
        if (rawHref == null) return null;

        // The export href is frequently relative (e.g. "?feed=xlsx&team_id=746561"),
        // so it must be resolved against the team page URL before it's fetchable.
        return IbbaUrlHelper.Resolve(teamPageUrl, rawHref);
    }

    /// <summary>
    /// Downloads the .xlsx bytes from the export URL and reads every row into IbbaGameRow
    /// objects.
    ///
    /// Real header row (verified against a live export):
    /// ליגה | Code | Week Day | תאריך | מחזור | Time | Home Team | Home Team Code |
    /// Away Team | Away Team Code | Venue | Home Score | Away Score
    /// </summary>
    public async Task<List<IbbaGameRow>> DownloadAndParseGamesAsync(string excelUrl)
    {
        var games = new List<IbbaGameRow>();

        byte[] fileBytes = await _http.GetByteArrayAsync(excelUrl);

        using var stream = new MemoryStream(fileBytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        var rows = worksheet.RangeUsed()?.RowsUsed().Skip(1); // skip header
        if (rows == null) return games;

        foreach (var row in rows)
        {
            // Skip fully blank rows
            if (row.Cells().All(c => string.IsNullOrWhiteSpace(c.GetString())))
                continue;

            games.Add(new IbbaGameRow
            {
                League = Decoded(row.Cell(1)),
                Code = Decoded(row.Cell(2)),
                WeekDay = Decoded(row.Cell(3)),
                Date = Decoded(row.Cell(4)),
                Round = Decoded(row.Cell(5)),
                Time = Decoded(row.Cell(6)),
                HomeTeam = Decoded(row.Cell(7)),
                HomeTeamCode = Decoded(row.Cell(8)),
                AwayTeam = Decoded(row.Cell(9)),
                AwayTeamCode = Decoded(row.Cell(10)),
                Venue = Decoded(row.Cell(11)),
                HomeScore = ParseNullableInt(row.Cell(12).GetString()),
                AwayScore = ParseNullableInt(row.Cell(13).GetString()),
            });
        }

        return games;
    }

    // The site's own Excel export bakes literal HTML entities into cell text
    // (e.g. a team named מכבי פ"ת comes back as "מכבי פ&quot;ת"), instead of
    // the real character - so every text cell needs HTML-decoding, not just trimming.
    private static string Decoded(IXLCell cell) => WebUtility.HtmlDecode(cell.GetString()).Trim();

    private static int? ParseNullableInt(string text)
    {
        text = text.Trim();
        return int.TryParse(text, out var value) ? value : null;
    }

    /// <summary>
    /// The canonical team name, read from the team's own page &lt;h1&gt;. Used instead of
    /// whatever text a link to this team carried on some OTHER page - the player page's
    /// "רשאי/ת" link, for example, renders as "TeamName - LeagueName", not just the team
    /// name, which broke exact-match lookups (like the crest) that assumed a plain name.
    /// </summary>
    public string? GetTeamName(HtmlDocument doc)
    {
        var h1 = doc.DocumentNode.SelectSingleNode("//h1");
        var name = h1?.InnerText.Trim();
        return name == null ? null : WebUtility.HtmlDecode(name);
    }

    /// <summary>
    /// Finds this team's crest image on an already-loaded team page. The page embeds
    /// several small widgets (upcoming games, standings snippets) that each render a
    /// &lt;span class="team-logo" title="{team name}"&gt;&lt;img data-src="..."&gt; per team
    /// shown. We match the span whose title equals this team's own name and read its
    /// img's data-src (the real image URL - src itself is a lazy-load placeholder SVG).
    /// Not every team page renders this widget, so null is a normal/expected result.
    /// </summary>
    public string? FindTeamLogo(HtmlDocument doc, string teamPageUrl, string teamName)
    {
        var span = doc.DocumentNode
            .SelectNodes("//span[contains(@class,'team-logo')]")
            ?.FirstOrDefault(s => WebUtility.HtmlDecode(s.GetAttributeValue("title", "")).Trim() == teamName.Trim());

        var img = span?.SelectSingleNode(".//img");
        var dataSrc = img?.GetAttributeValue("data-src", null);
        return string.IsNullOrWhiteSpace(dataSrc) ? null : IbbaUrlHelper.Resolve(teamPageUrl, dataSrc);
    }

    /// <summary>
    /// Convenience wrapper for callers that only ever need the crest for one
    /// team in isolation (the background crest backfill, which handles many
    /// unrelated teams one at a time) - fetches the page and extracts just
    /// the logo. IbbaReportBuilder needs several things off the same page, so
    /// it loads the page itself once and calls FindTeamLogo directly instead.
    /// </summary>
    public async Task<string?> FindTeamLogoUrlAsync(string teamPageUrl, string teamName)
    {
        var doc = await LoadTeamPageAsync(teamPageUrl);
        return FindTeamLogo(doc, teamPageUrl, teamName);
    }

    /// <summary>
    /// Resolves a team page from nothing but its numeric id (e.g. a game row's
    /// HomeTeamCode/AwayTeamCode) - the site redirects "/team/{id}" to the real,
    /// slugged page ("/team/{id}-name/"), and that redirected URL is what's
    /// returned, so it lines up with the TeamUrl a normal league/standings sync
    /// would have produced for the same team. Used for opponents that never show
    /// up in our own league's standings (a cup or friendly game against a team
    /// from a different league/age group). Name and crest both come off this
    /// one page load - no separate fetch for the logo.
    /// </summary>
    public async Task<(string CanonicalUrl, string Name, string? LogoUrl)?> ResolveTeamByIdAsync(string teamId)
    {
        using var response = await _http.GetAsync($"https://ibasketball.co.il/team/{teamId}");
        if (!response.IsSuccessStatusCode) return null;

        var canonicalUrl = response.RequestMessage?.RequestUri?.ToString() ?? $"https://ibasketball.co.il/team/{teamId}";
        var html = await response.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var h1 = doc.DocumentNode.SelectSingleNode("//h1");
        var name = h1 == null ? null : WebUtility.HtmlDecode(h1.InnerText).Trim();
        if (string.IsNullOrWhiteSpace(name)) return null;

        var logoUrl = FindTeamLogo(doc, canonicalUrl, name);
        return (canonicalUrl, name, logoUrl);
    }

    /// <summary>
    /// Resolves a league's URL from its name, using the searchable league directory embedded
    /// on an already-loaded team page (a filter widget listing every league in the team's
    /// age/gender category, e.g. class="league data-item"). This works for ANY team - main
    /// or רשאי - unlike the player page, which only labels the main team's league.
    /// </summary>
    public string? FindLeagueUrl(HtmlDocument doc, string teamPageUrl, string leagueName)
    {
        var link = doc.DocumentNode
            .SelectNodes("//a[contains(@class,'league') and contains(@class,'data-item')]")
            ?.FirstOrDefault(a => WebUtility.HtmlDecode(a.InnerText).Trim() == leagueName.Trim());

        if (link == null) return null;
        var rawHref = link.GetAttributeValue("href", "");
        return string.IsNullOrWhiteSpace(rawHref) ? null : IbbaUrlHelper.Resolve(teamPageUrl, rawHref);
    }
}
