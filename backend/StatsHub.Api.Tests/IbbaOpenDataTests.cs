using System.Net;
using StatsHub.Api.IbbaScraping;
using StatsHub.Api.Leagues;
using StatsHub.Api.Services;

namespace StatsHub.Api.Tests;

// IBBA's open data (wp-json) in place of team and league pages - against a
// fake site that answers only what's listed, so a page the sync no longer
// needs would show up as a failed (404) request.
public class IbbaOpenDataTests
{
    private const string Api = "https://ibasketball.co.il/wp-json/sportspress/v2";
    private const string TeamUrl = "https://ibasketball.co.il/team/4180-מכבי-חיפה/";
    private const string PlayerUrl = "https://ibasketball.co.il/player/246acb577fbe7bd2-30377483/";

    private sealed class Site(Dictionary<string, Func<HttpContent>> pages) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Requested) Requested.Add(url);
            return Task.FromResult(pages.TryGetValue(url, out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = content(), RequestMessage = request }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }
    }

    private sealed class Factory(HttpMessageHandler site) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(site, disposeHandler: false);
    }

    private static Func<HttpContent> Json(string json) => () => new StringContent(json);

    // A team as the open data gives it: title, (percent-encoded) link, leagues, crest.
    private static string TeamJson(int id, string name, string url, int[] leagues, string? crest = "https://ibasketball.co.il/crest-4180.png")
    {
        var media = crest == null ? ""
            : ",\"_embedded\":{\"wp:featuredmedia\":[{\"source_url\":\"" + crest + "\",\"media_details\":{\"sizes\":{\"thumbnail\":{\"source_url\":\"" + crest.Replace(".png", "-150x150.png") + "\"}}}}]}";
        return "{\"id\":" + id + ",\"title\":{\"rendered\":\"" + name + "\"},\"link\":\"" + Uri.EscapeUriString(url) + "\",\"leagues\":[" + string.Join(',', leagues) + "]" + media + "}";
    }

    // A player on מכבי חיפה, whose games and league table all come from the open data.
    private static Dictionary<string, Func<HttpContent>> HaifaSite() => new()
    {
        [PlayerUrl] = () => new StringContent("<html><body><h1>נרי פלד</h1><div>קבוצה נוכחית <a href='/team/4180-מכבי-חיפה/'>מכבי חיפה</a></div></body></html>"),
        [$"{Api}/teams?slug=4180-מכבי-חיפה&_embed=wp:featuredmedia&_fields=id,title,link,leagues,_links,_embedded"] =
            Json($"[{TeamJson(168967, "מכבי חיפה", TeamUrl, [12764, 119542, 119959, 120232])}]"),
        [$"{TeamUrl}?feed=xlsx&team_id=168967"] = () => IbbaLayoutTests.Sheet(
            ["ליגה", "Code", "Week Day", "תאריך", "מחזור", "Time", "Home Team", "Home Team Code", "Away Team", "Away Team Code", "Venue", "Home Score", "Away Score"],
            ["נערים ב לאומית צפון", "1001", "Sun", "11-10-2026", "1", "18:00", "הפועל מגידו צפון", "12018", "מכבי חיפה", "4180", "Gym", "78", "60"],
            ["גביע המדינה לנערים ב", "1002", "Sun", "18-10-2026", "1", "18:00", "מכבי חיפה", "4180", "הפועל נשר", "7983", "Gym", "", ""]),
        // The same league name every season - the newest (119959) is this season's.
        [$"{Api}/leagues?include=12764,119542,119959,120232&per_page=100&_fields=id,name,link"] = Json("""
            [{"id":12764,"name":"נערים ב לאומית צפון","link":"https://ibasketball.co.il/league/2019-201/"},
             {"id":119542,"name":"נערים ב לאומית צפון","link":"https://ibasketball.co.il/league/2025-201/"},
             {"id":119959,"name":"נערים ב לאומית צפון","link":"https://ibasketball.co.il/league/2026-201/"},
             {"id":120232,"name":"גביע המדינה לנערים ב","link":"https://ibasketball.co.il/league/2026-901/"}]
            """),
        [$"{Api}/tables?leagues=119959&_fields=id,title,data"] = Json("""
            [{"id":1501421,"title":{"rendered":"נערים ב לאומית צפון &#8211; רגילה &#8211; רגילה &#8211; 2026"},"data":{
              "168967":{"gp":2,"w":1,"l":1,"lt":0,"bf":136,"ba":141,"bd":-5,"pts":3,"pos":2,"name":"מכבי חיפה"},
              "880927":{"gp":3,"w":2,"l":1,"lt":0,"bf":221,"ba":197,"bd":24,"pts":5,"pos":1,"name":"הפועל מגידו צפון"},
              "0":{"pos":"מיקום","name":"קבוצה","gp":"מש׳","w":"ניצ׳","l":"הפ׳","lt":"טכני","bf":"קלעה","ba":"ספגה","bd":"הפרש","pts":"נק׳"}}}]
            """),
        [$"{Api}/teams?include=168967,880927&per_page=100&_fields=id,link"] = Json(
            """[{"id":168967,"link":"https://ibasketball.co.il/team/4180-%d7%9e%d7%9b%d7%91%d7%99-%d7%97%d7%99%d7%a4%d7%94/"},{"id":880927,"link":"https://ibasketball.co.il/team/12018-%d7%94%d7%a4%d7%95%d7%a2%d7%9c-%d7%9e%d7%92%d7%99%d7%93%d7%95-%d7%a6%d7%a4%d7%95%d7%9f/"}]"""),
    };

    [Fact]
    public async Task A_sync_reads_teams_leagues_and_tables_from_the_open_data_and_no_team_or_league_page()
    {
        var site = new Site(HaifaSite());
        var ibba = new IbbaLeagueProvider(new Factory(site));

        var (_, pages) = await ibba.LoadTeamsAsync(PlayerUrl);
        var report = await ibba.LoadGamesAndStandingsAsync(pages.Single());

        var team = report.Team;
        Assert.Equal(("מכבי חיפה", "https://ibasketball.co.il/crest-4180-150x150.png"), (team.TeamName, team.TeamLogoUrl));
        Assert.Equal(("נערים ב לאומית צפון", "https://ibasketball.co.il/league/2026-201/"), (team.LeagueName, team.LeagueUrl));
        Assert.Equal(["1001", "1002"], report.Games.Select(g => g.Code).ToArray());
        Assert.Equal(
            [(1, "הפועל מגידו צפון", "https://ibasketball.co.il/team/12018-הפועל-מגידו-צפון/", 3, 2, 1, 221, 197, 24, 5),
             (2, "מכבי חיפה", TeamUrl, 2, 1, 1, 136, 141, -5, 3)],
            report.Standings.Select(s => (s.Position, s.TeamName, s.TeamUrl, s.GamesPlayed, s.Wins, s.Losses, s.PointsFor, s.PointsAgainst, s.Diff, s.LeaguePoints)).ToArray());
        Assert.Equal((2, 3), (report.TeamStanding?.Position, report.TeamStanding?.LeaguePoints));

        // Nothing but the player's page, the spreadsheet and the open data - every request answered.
        Assert.DoesNotContain(site.Requested, url => url.StartsWith("https://ibasketball.co.il/league/") || url == TeamUrl);
        Assert.Equal(6, site.Requested.Count);
    }

    [Fact]
    public async Task Without_the_open_data_the_team_and_league_pages_are_read_as_before()
    {
        var pages = HaifaSite().Where(p => !p.Key.StartsWith(Api)).ToDictionary();
        pages[TeamUrl] = () => new StringContent("""
            <html><h1>מכבי חיפה</h1><span class="team-logo" title="מכבי חיפה"><img data-src="/crest-page.png"></span>
            <a href="?feed=xlsx&team_id=168967">יצוא לאקסל</a>
            <a class="league data-item" href="/league/2026-201/">נערים ב לאומית צפון</a></html>
            """);
        pages["https://ibasketball.co.il/league/2026-201/"] = () => new StringContent("""
            <html><table><tr><th>מיקום</th><th>קבוצה</th><th>מש'</th><th>ניצ'</th><th>הפ'</th><th>טכני</th><th>קלעה</th><th>ספגה</th><th>הפרש</th><th>נק'</th></tr>
            <tr><td>1</td><td><a href="/team/4180-מכבי-חיפה/">מכבי חיפה</a></td><td>2</td><td>1</td><td>1</td><td>0</td><td>136</td><td>141</td><td>-5</td><td>3</td></tr></table></html>
            """);
        var ibba = new IbbaLeagueProvider(new Factory(new Site(pages)));

        var (_, teamPages) = await ibba.LoadTeamsAsync(PlayerUrl);
        var report = await ibba.LoadGamesAndStandingsAsync(teamPages.Single());

        Assert.Equal("https://ibasketball.co.il/crest-page.png", report.Team.TeamLogoUrl);
        Assert.Equal("https://ibasketball.co.il/league/2026-201/", report.Team.LeagueUrl);
        Assert.Equal(2, report.Games.Count);
        Assert.Equal(3, report.TeamStanding?.LeaguePoints);
    }

    [Fact]
    public async Task Results_soon_after_a_game_read_the_spreadsheet_without_the_team_page()
    {
        var site = new Site(HaifaSite());

        var games = await new IbbaLeagueProvider(new Factory(site)).LoadTeamGamesAsync(TeamUrl);

        Assert.Equal(2, games.Count);
        Assert.DoesNotContain(TeamUrl, site.Requested);
    }

    [Fact]
    public async Task A_cup_opponent_is_found_by_its_id_and_name_in_the_open_data()
    {
        const string nesher = "https://ibasketball.co.il/team/7983-הפועל-נשר/";
        var site = new Site(new()
        {
            [$"{Api}/teams?search=הפועל נשר&per_page=50&_embed=wp:featuredmedia&_fields=id,title,link,leagues,_links,_embedded"] = Json(
                $"[{TeamJson(1, "הפועל נשר", "https://ibasketball.co.il/team/7984-הפועל-נשר-נוער/", [1])},{TeamJson(597672, "הפועל נשר", nesher, [119959], "https://ibasketball.co.il/nesher.png")}]"),
        });

        var resolved = await new IbbaLeagueProvider(new Factory(site)).ResolveTeamByIdAsync("7983", "הפועל נשר");

        Assert.Equal((nesher, "הפועל נשר", "https://ibasketball.co.il/nesher-150x150.png"), resolved);
        Assert.DoesNotContain(site.Requested, url => url.Contains("/team/7983"));
    }

    // ---- The games spreadsheet: a copy at most a few minutes old for a parent waiting ----

    private sealed class CachingSite(TimeSpan? age, string? cacheStatus = "HIT") : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent("xlsx") };
            var fresh = request.RequestUri!.Query.Contains("_sh=");
            if (!fresh && age != null) response.Headers.Age = age;
            if (!fresh && cacheStatus != null) response.Headers.Add("cf-cache-status", cacheStatus);
            return Task.FromResult(response);
        }
    }

    private const string Spreadsheet = "https://ibasketball.co.il/team/4180-x/?feed=xlsx&team_id=168967";

    [Fact]
    public async Task A_parent_waiting_gets_a_saved_copy_of_the_schedule_only_when_it_is_recent()
    {
        var recent = new CachingSite(TimeSpan.FromMinutes(3));
        await new HttpClient(new IbbaNoCacheHandler { InnerHandler = recent }).GetAsync(Spreadsheet);
        Assert.Equal([Spreadsheet], recent.Requests.Select(r => r.RequestUri!.ToString()));

        var old = new CachingSite(TimeSpan.FromMinutes(25));
        await new HttpClient(new IbbaNoCacheHandler { InnerHandler = old }).GetAsync(Spreadsheet);
        Assert.Equal(2, old.Requests.Count);
        Assert.Contains("_sh=", old.Requests[1].RequestUri!.Query); // refetched fresh
        Assert.True(old.Requests[1].Headers.CacheControl!.NoCache);

        var builtNow = new CachingSite(age: null, cacheStatus: "MISS");
        await new HttpClient(new IbbaNoCacheHandler { InnerHandler = builtNow }).GetAsync(Spreadsheet);
        Assert.Single(builtNow.Requests);
    }

    [Fact]
    public async Task Background_jobs_always_read_the_schedule_fresh()
    {
        var site = new CachingSite(TimeSpan.FromMinutes(1));
        using (IbbaFreshness.RequireFresh())
            await new HttpClient(new IbbaNoCacheHandler { InnerHandler = site }).GetAsync(Spreadsheet);

        Assert.Contains("_sh=", site.Requests.Single().RequestUri!.Query);
        Assert.False(IbbaFreshness.FreshRequired); // only for that job
    }

    [Fact]
    public void Each_player_page_is_read_one_night_a_week()
    {
        var monday = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 50; i++)
        {
            var link = Guid.NewGuid();
            Assert.Equal(1, Enumerable.Range(0, 7).Count(d => IbbaService.IsPlayerPageNight(link, monday.AddDays(d))));
        }
    }
}
