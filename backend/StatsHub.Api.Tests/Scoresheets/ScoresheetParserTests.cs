using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsHub.Api.Scoresheets;

namespace StatsHub.Api.Tests.Scoresheets;

// Reads a real league scoresheet (Kfar Yona vs Tel Mond, 19/10/2026, final 70-54) straight from
// the PDF's vector data. The expected values were produced by the reference parser
// (tools/scoresheet-reference) and checked by hand against the printed sheet.
public class ScoresheetParserTests
{
    private const string Fixture = "ibba-youth-2026-10-19-kfar-yona-tel-mond";

    private static string FixturePath(string extension) =>
        Path.Combine(AppContext.BaseDirectory, "Scoresheets", "Fixtures", Fixture + extension);

    private static ParsedScoresheet Parse() => ScoresheetParser.Parse(File.ReadAllBytes(FixturePath(".pdf")));

    [Fact]
    public void The_sheet_passes_every_cross_check()
    {
        var sheet = Parse();

        Assert.True(sheet.IsValid, string.Join("; ", sheet.Errors));
        Assert.Empty(sheet.Warnings);
    }

    [Fact]
    public void Header_scores_and_quarters_are_read()
    {
        var sheet = Parse();

        Assert.Equal("19/10/2026", sheet.Date);
        Assert.Equal("19:00", sheet.Time);
        Assert.Equal(70, sheet.HomeFinal);
        Assert.Equal(54, sheet.AwayFinal);
        Assert.Equal(new[] { (18, 10), (22, 15), (18, 11), (12, 18) },
            sheet.Quarters.Select(q => (q.Home!.Value, q.Away!.Value)).ToArray());
        Assert.Equal(70, sheet.Home.Points);
        Assert.Equal(54, sheet.Away.Points);
    }

    [Theory]
    // team, jersey number, points, free throws, twos, threes
    [InlineData("home", 28, 14, 6, 1, 2)]
    [InlineData("home", 19, 13, 3, 2, 2)]
    [InlineData("home", 10, 11, 0, 4, 1)]
    [InlineData("home", 2, 0, 0, 0, 0)]
    [InlineData("away", 16, 18, 5, 5, 1)]
    [InlineData("away", 8, 11, 2, 3, 1)]
    [InlineData("away", 9, 3, 0, 0, 1)]
    [InlineData("away", 20, 0, 0, 0, 0)]
    public void Player_points_split_into_free_throws_twos_and_threes(
        string team, int number, int points, int freeThrows, int twos, int threes)
    {
        var sheet = Parse();
        var player = (team == "home" ? sheet.Home : sheet.Away).Players.Single(p => p.Number == number);

        Assert.Equal(points, player.Points);
        Assert.Equal(freeThrows, player.FreeThrows);
        Assert.Equal(twos, player.TwoPointers);
        Assert.Equal(threes, player.ThreePointers);
    }

    [Fact]
    public void Rosters_carry_player_codes_names_starters_and_captains()
    {
        var sheet = Parse();

        var kahan = sheet.Away.Players.Single(p => p.Number == 16);
        Assert.Equal("188", kahan.Code);
        Assert.Equal("כאהן", kahan.LastName);
        Assert.Equal("א", kahan.FirstInitial);
        Assert.True(kahan.Starter);

        Assert.Equal(11, sheet.Home.Players.Count);
        Assert.Equal(15, sheet.Away.Players.Count);
        Assert.Equal(10, sheet.Home.Players.Single(p => p.Captain).Number);
        Assert.Equal(8, sheet.Away.Players.Single(p => p.Captain).Number);
        Assert.Equal(new[] { 10, 19, 24, 28, 30 }, sheet.Home.Players.Where(p => p.Starter).Select(p => p.Number).ToArray());
        Assert.Equal(new[] { 2, 6, 8, 16, 19 }, sheet.Away.Players.Where(p => p.Starter).Select(p => p.Number).ToArray());
        // #20 never entered the game.
        Assert.False(sheet.Away.Players.Single(p => p.Number == 20).Played);
    }

    private static ParsedScoresheet ParseFixture(string name) =>
        ScoresheetParser.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Scoresheets", "Fixtures", name + ".pdf")));

    [Fact]
    public void Jersey_0_and_00_are_two_players()
    {
        // Megiddo has #0 (Ariav, scored a basket) and #00 (Luria, did not play): read as numbers
        // both were 0 and the basket counted twice - 80 points against a running score of 78.
        var sheet = ParseFixture("ibba-boys-b-2026-10-11-haifa-megiddo");

        Assert.True(sheet.IsValid, string.Join("; ", sheet.Errors));
        Assert.Equal((60, 78), (sheet.HomeFinal!.Value, sheet.AwayFinal!.Value));
        Assert.Equal(2, sheet.Away.Players.Single(p => p.Code == "065").Points);
        Assert.Equal(0, sheet.Away.Players.Single(p => p.Code == "674").Points);
        Assert.Equal(78, sheet.Away.Players.Sum(p => p.Points));
    }

    [Fact]
    public void A_captain_marked_C_keeps_the_name_and_initial_apart()
    {
        // "סמט (C), א." - the short captain marker shifts the words; the initial is still the
        // one-letter token. His 14 points match IBBA's own box score for this game.
        var sheet = ParseFixture("ibba-artzit-2026-10-11-netzer-sereni-maale-adumim");
        var samet = sheet.Away.Players.Single(p => p.Code == "800");

        Assert.True(sheet.IsValid, string.Join("; ", sheet.Errors));
        Assert.Equal(("סמט", "א", 12), (samet.LastName, samet.FirstInitial, samet.Number));
        Assert.True(samet.Captain);
        Assert.True(samet.Starter);
        Assert.Equal((14, 0, 1, 4, 4), (samet.Points, samet.FreeThrows, samet.TwoPointers, samet.ThreePointers, samet.Fouls));
        Assert.Equal(ScoresheetMatchStatus.Matched, ScoresheetNameMatcher.Match("אברהם זאב", "סמט", sheet.Away.Players).Status);
    }

    [Fact]
    public void The_whole_result_matches_the_verified_expected_output()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var actual = JsonNode.Parse(JsonSerializer.Serialize(Parse(), options))!.AsObject();
        // IsValid is computed from Errors, not read from the sheet - checked on its own.
        Assert.True(actual["isValid"]!.GetValue<bool>());
        actual.Remove("isValid");
        var expected = JsonNode.Parse(File.ReadAllText(FixturePath(".expected.json")));

        Assert.True(JsonNode.DeepEquals(expected, actual),
            "Parsed sheet differs from the expected JSON.\nACTUAL:\n" + actual!.ToJsonString(options));
    }
}
