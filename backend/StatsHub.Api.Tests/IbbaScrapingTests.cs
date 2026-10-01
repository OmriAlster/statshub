using System.Net;
using ClosedXML.Excel;
using StatsHub.Api.IbbaScraping;

namespace StatsHub.Api.Tests;

// The IBBA parsing, against generated files served by a fake HTTP handler -
// never the real ibasketball.co.il.
public class IbbaScrapingTests
{
    private sealed class FakeSite : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpContent> _pages;
        public FakeSite(Dictionary<string, HttpContent> pages) => _pages = pages;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_pages.TryGetValue(request.RequestUri!.ToString(), out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = content }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static ByteArrayContent GamesExport(params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("games");
        string[] header = ["ליגה", "Code", "Week Day", "תאריך", "מחזור", "Time", "Home Team", "Home Team Code", "Away Team", "Away Team Code", "Venue", "Home Score", "Away Score"];
        for (var c = 0; c < header.Length; c++) sheet.Cell(1, c + 1).Value = header[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                sheet.Cell(r + 2, c + 1).Value = rows[r][c];
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new ByteArrayContent(stream.ToArray());
    }

    [Fact]
    public async Task Duplicate_rows_in_the_export_become_one_game_each()
    {
        // The real export lists a large share of games twice - this used to
        // make every sync fail silently and save zero games.
        const string url = "https://ibba.test/export.xlsx";
        string[] game1 = ["נערים א", "1001", "Sun", "18-10-2026", "2", "19:30", "Home", "10", "Away", "20", "Gym", "", ""];
        string[] game2 = ["נערים א", "1002", "Sun", "25-10-2026", "3", "19:30", "Away", "20", "Home", "10", "Gym", "70", "64"];
        var http = new HttpClient(new FakeSite(new() { [url] = GamesExport(game1, game1, game2, game2, game2) }));

        var games = await new IbbaTeamScraper(http).DownloadAndParseGamesAsync(url);

        Assert.Equal(["1001", "1002"], games.Select(g => g.Code).ToArray());
        Assert.Equal(70, games[1].HomeScore);
        Assert.Null(games[0].HomeScore);
    }

    [Fact]
    public async Task Html_entities_in_team_names_are_decoded()
    {
        const string url = "https://ibba.test/export.xlsx";
        var http = new HttpClient(new FakeSite(new() { [url] = GamesExport(["ליגה", "1", "Sun", "1-1-2027", "1", "18:00", "מכבי פ&quot;ת", "1", "Other", "2", "Gym", "", ""]) }));

        var games = await new IbbaTeamScraper(http).DownloadAndParseGamesAsync(url);

        Assert.Equal("מכבי פ\"ת", games.Single().HomeTeam);
    }

    [Fact]
    public async Task Cup_games_are_recognised_by_league_name()
    {
        const string url = "https://ibba.test/export.xlsx";
        var http = new HttpClient(new FakeSite(new() { [url] = GamesExport(
            ["גביע האיגוד לנערים א", "1", "Sun", "1-1-2027", "1", "18:00", "A", "1", "B", "2", "Gym", "", ""],
            ["נערים א מחוזית שרון", "2", "Sun", "8-1-2027", "2", "18:00", "A", "1", "B", "2", "Gym", "", ""]) }));

        var games = await new IbbaTeamScraper(http).DownloadAndParseGamesAsync(url);

        Assert.True(games[0].IsCup);
        Assert.False(games[1].IsCup);
    }

    [Fact]
    public void The_excel_export_link_is_read_off_the_team_page_and_resolved()
    {
        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml("<html><h1>מכבי תל מונד</h1><a href='?feed=xlsx&team_id=746561'>יצוא לאקסל</a></html>");
        var scraper = new IbbaTeamScraper(new HttpClient());

        Assert.Equal("https://ibasketball.co.il/team/13352-x/?feed=xlsx&team_id=746561", scraper.FindExcelExportUrl(doc, "https://ibasketball.co.il/team/13352-x/"));
        Assert.Equal("מכבי תל מונד", scraper.GetTeamName(doc));
    }

    // Records what actually went out over the wire, and answers every request
    // with a team page.
    private sealed class RecordingSite : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("<html><h1>מכבי תל מונד</h1></html>"),
            });
        }
    }

    [Fact]
    public async Task The_games_spreadsheet_skips_their_caches_and_pages_use_them()
    {
        // IBBA sits behind Cloudflare + a page cache; a stale cached schedule
        // kept an old game time in production - so each spreadsheet request
        // is unique. Pages may come from the cache: IBBA takes ~20s to build
        // one itself, against ~0.3s cached.
        var site = new RecordingSite();
        var http = new HttpClient(new IbbaNoCacheHandler { InnerHandler = site });

        await http.GetAsync("https://ibasketball.co.il/team/13352-x/?feed=xlsx&team_id=746561");
        await http.GetAsync("https://ibasketball.co.il/team/13352-x/?feed=xlsx&team_id=746561");
        await http.GetAsync("https://ibasketball.co.il/team/13352-x/");
        await http.GetAsync("https://ibasketball.co.il/player/abc-123/");
        await http.GetAsync("https://example.com/other");

        var uris = site.Requests.Select(r => r.RequestUri!).ToList();
        Assert.Contains("feed=xlsx&team_id=746561&_sh=", uris[0].Query);
        Assert.NotEqual(uris[0], uris[1]);
        Assert.True(site.Requests[0].Headers.CacheControl!.NoCache);
        Assert.Equal("https://ibasketball.co.il/team/13352-x/", uris[2].ToString());
        Assert.Equal("https://ibasketball.co.il/player/abc-123/", uris[3].ToString());
        Assert.Equal("https://example.com/other", uris[4].ToString()); // other sites untouched
    }

    [Fact]
    public async Task A_resolved_team_address_never_keeps_the_cache_buster()
    {
        var http = new HttpClient(new IbbaNoCacheHandler { InnerHandler = new RecordingSite() });

        var resolved = await new IbbaTeamScraper(http).ResolveTeamByIdAsync("13352");

        Assert.NotNull(resolved);
        Assert.Equal("https://ibasketball.co.il/team/13352", resolved!.Value.CanonicalUrl);
        Assert.Equal("מכבי תל מונד", resolved.Value.Name);
    }

    [Fact]
    public void A_standing_row_matches_a_team_exactly_not_by_prefix()
    {
        var row = new IbbaStandingRow { TeamName = "מכבי בקה גת" };
        Assert.False(row.IsTeam(null, "מכבי בקה"));
        Assert.True(row.IsTeam(null, "מכבי בקה גת"));
    }
}

public class IbbaSyncTrackerTests
{
    [Fact]
    public void A_sync_asked_for_while_one_runs_runs_again_afterwards()
    {
        var tracker = new StatsHub.Api.Services.IbbaSyncTracker();
        var link = Guid.NewGuid();

        Assert.True(tracker.TryStart(link));      // starts
        Assert.True(tracker.IsRunning(link));     // "loading games"
        Assert.False(tracker.TryStart(link));     // e.g. a team linked in the pop-up meanwhile
        Assert.True(tracker.Finish(link));        // -> run once more
        Assert.False(tracker.IsRunning(link));

        Assert.True(tracker.TryStart(link));
        Assert.False(tracker.Finish(link));       // nothing asked meanwhile -> done
    }
}
