using System.Net;
using ClosedXML.Excel;
using HtmlAgilityPack;
using StatsHub.Api.Services;
using StatsHub.Api.Leagues;

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
        using var parsing = RequestTimings.Time("parse-team-page");
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
    /// Downloads the .xlsx bytes from the export URL and reads every row into LeagueGame
    /// objects.
    ///
    /// Real header row (verified against a live export):
    /// ליגה | Code | Week Day | תאריך | מחזור | Time | Home Team | Home Team Code |
    /// Away Team | Away Team Code | Venue | Home Score | Away Score
    /// </summary>
    // The spreadsheet library does a lot of one-time setup on its first read
    // (measured at 0.5-5s), which landed on whoever synced first after each
    // deploy/restart. Reading a tiny made-up sheet at startup takes that hit
    // in the background instead.
    public static void WarmUpSpreadsheetReader()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("Games").Cell(1, 1).Value = "warm-up";
            workbook.SaveAs(stream);
        }
        stream.Position = 0;
        using var reread = new XLWorkbook(stream);
        _ = reread.Worksheet(1).RangeUsed()?.RowsUsed().Count();
    }

    // The export's columns, found by their header - so a reordered column is
    // still read right, and a renamed or missing one stops the sync
    // (LeagueLayoutException) instead of filling games with another column's data.
    private static readonly string[] GameColumns =
        ["ליגה", "Code", "Week Day", "תאריך", "מחזור", "Time", "Home Team", "Home Team Code", "Away Team", "Away Team Code", "Venue", "Home Score", "Away Score"];

    private static readonly System.Text.RegularExpressions.Regex DateFormat = new(@"^\d{1,2}-\d{1,2}-\d{4}$");
    private static readonly System.Text.RegularExpressions.Regex TimeFormat = new(@"^\d{1,2}:\d{2}$");

    public async Task<List<LeagueGame>> DownloadAndParseGamesAsync(string excelUrl)
    {
        var fileBytes = await _http.GetByteArrayAsync(excelUrl);
        try
        {
            return ParseGames(fileBytes);
        }
        catch (Exception ex) when (ex is not LeagueLayoutException && !IbbaFreshness.FreshRequired)
        {
            // A file that won't open at all (once, IBBA's copy came back
            // broken - "Invalid character in the given encoding"): one more
            // try with a freshly built one before the sync gives up.
            using var fresh = IbbaFreshness.RequireFresh();
            return ParseGames(await _http.GetByteArrayAsync(excelUrl));
        }
    }

    private static List<LeagueGame> ParseGames(byte[] fileBytes)
    {
        var games = new List<LeagueGame>();
        using var parsing = RequestTimings.Time("parse-games-xlsx");

        using var stream = new MemoryStream(fileBytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        var used = worksheet.RangeUsed()?.RowsUsed().ToList();
        if (used == null || used.Count == 0) return games; // a team with no games yet

        var header = used[0].Cells().ToDictionary(c => Decoded(c), c => c.Address.ColumnNumber);
        var missing = GameColumns.Where(name => !header.ContainsKey(name)).ToList();
        if (missing.Count > 0)
            throw new LeagueLayoutException($"games spreadsheet is missing column(s) {string.Join(", ", missing)}; it has {string.Join(", ", header.Keys)}");
        string Text(IXLRangeRow row, string column) => Decoded(row.Cell(header[column] - row.FirstCell().Address.ColumnNumber + 1));

        foreach (var row in used.Skip(1))
        {
            // Skip fully blank rows
            if (row.Cells().All(c => string.IsNullOrWhiteSpace(c.GetString())))
                continue;

            var game = new LeagueGame
            {
                League = Text(row, "ליגה"),
                Code = Text(row, "Code"),
                WeekDay = Text(row, "Week Day"),
                Date = Text(row, "תאריך"),
                Round = Text(row, "מחזור"),
                Time = Text(row, "Time"),
                HomeTeam = Text(row, "Home Team"),
                HomeTeamCode = Text(row, "Home Team Code"),
                AwayTeam = Text(row, "Away Team"),
                AwayTeamCode = Text(row, "Away Team Code"),
                Venue = Text(row, "Venue"),
                HomeScore = ParseScore(Text(row, "Home Score"), row),
                AwayScore = ParseScore(Text(row, "Away Score"), row),
            };
            game.IsCup = game.League.Contains("גביע"); // "cup" - IBBA names its cup competitions so

            // A date or time in another format would otherwise become "now".
            if (!DateFormat.IsMatch(game.Date) || (game.Time.Length > 0 && !TimeFormat.IsMatch(game.Time)))
                throw new LeagueLayoutException($"game {game.Code} has date \"{game.Date}\" / time \"{game.Time}\", expected dd-mm-yyyy and hh:mm");
            games.Add(game);
        }

        // The export genuinely lists a large fraction of games twice (verified
        // against a live export - identical in every column, not a scheduling
        // quirk). Games.IbbaGameCode has a unique index, so handing every raw
        // row to UpsertGamesAsync meant the very first duplicate in a batch
        // threw a DbUpdateException that rolled back and silently dropped
        // EVERY game in that sync, not just the duplicate - a team could sync
        // "successfully" (no LastSyncError) while ending up with zero games
        // saved. Dedup by Code here, once, at the source, so every caller of
        // this method gets clean rows. Rows with no code at all (if any) are
        // left alone since they can't collide with anything.
        return games
            .GroupBy(g => g.Code)
            .SelectMany(g => string.IsNullOrEmpty(g.Key) ? g : g.Take(1))
            .ToList();
    }

    // The site's own Excel export bakes literal HTML entities into cell text
    // (e.g. a team named מכבי פ"ת comes back as "מכבי פ&quot;ת"), instead of
    // the real character - so every text cell needs HTML-decoding, not just trimming.
    private static string Decoded(IXLCell cell) => WebUtility.HtmlDecode(cell.GetString()).Trim();

    // Empty until the game is played; anything else that isn't a number means
    // the column holds something new.
    private static int? ParseScore(string text, IXLRangeRow row)
    {
        if (text.Length == 0) return null;
        return int.TryParse(text, out var value)
            ? value
            : throw new LeagueLayoutException($"row {row.RowNumber()} has score \"{text}\"");
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
        using var finding = RequestTimings.Time("find-crest");
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

        // The final (redirected) address, minus any query - requests to IBBA
        // carry a cache-busting parameter (IbbaNoCacheHandler) that must not
        // end up in the stored team URL.
        var finalUri = response.RequestMessage?.RequestUri;
        var canonicalUrl = finalUri != null ? IbbaNoCacheHandler.WithoutQuery(finalUri) : $"https://ibasketball.co.il/team/{teamId}";
        var html = await response.Content.ReadAsStringAsync();
        using var parsing = RequestTimings.Time("parse-team-page");
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
