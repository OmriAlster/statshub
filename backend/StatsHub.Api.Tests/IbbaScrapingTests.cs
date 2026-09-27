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

    [Fact]
    public void A_standing_row_matches_a_team_exactly_not_by_prefix()
    {
        var row = new IbbaStandingRow { TeamName = "מכבי בקה גת" };
        Assert.False(row.IsTeam(null, "מכבי בקה"));
        Assert.True(row.IsTeam(null, "מכבי בקה גת"));
    }
}
