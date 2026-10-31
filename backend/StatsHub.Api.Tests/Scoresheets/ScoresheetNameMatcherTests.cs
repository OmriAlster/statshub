using StatsHub.Api.Scoresheets;

namespace StatsHub.Api.Tests.Scoresheets;

public class ScoresheetNameMatcherTests
{
    private static ScoresheetPlayer P(int number, string last, string initial, string code = "0") =>
        new(number, code, last, initial, false, true, false, 0, 0, 0, 0, 0);

    private static readonly IReadOnlyList<ScoresheetPlayer> Roster = new[]
    {
        P(16, "כאהן", "א", "188"),
        P(20, "כהן", "נ", "019"),
        P(4, "בראונר (ח)", "נ", "564"),
        P(19, "ברש אור", "י", "547"),
    };

    [Fact]
    public void Last_name_and_first_initial_find_the_player()
    {
        var match = ScoresheetNameMatcher.Match("אורי", "כאהן", Roster);

        Assert.Equal(ScoresheetMatchStatus.Matched, match.Status);
        Assert.Equal("188", match.Player!.Code);
    }

    [Fact]
    public void Brackets_quotes_spaces_and_hyphens_do_not_matter()
    {
        Assert.Equal(4, ScoresheetNameMatcher.Match("נועם", "בראונר", Roster).Player!.Number);
        Assert.Equal(19, ScoresheetNameMatcher.Match("יוסי", "ברש-אור", Roster).Player!.Number);
    }

    [Fact]
    public void A_different_initial_is_not_a_match()
    {
        Assert.Equal(ScoresheetMatchStatus.NotFound, ScoresheetNameMatcher.Match("דוד", "כאהן", Roster).Status);
    }

    [Fact]
    public void Two_players_with_the_same_name_are_ambiguous_and_listed()
    {
        var twins = new[] { P(5, "לוי", "ד", "1"), P(15, "לוי", "ד", "2") };

        var match = ScoresheetNameMatcher.Match("דנה", "לוי", twins);

        Assert.Equal(ScoresheetMatchStatus.Ambiguous, match.Status);
        Assert.Null(match.Player);
        Assert.Equal(new[] { 5, 15 }, match.Candidates.Select(c => c.Number).ToArray());
    }

    [Fact]
    public void Missing_names_never_match()
    {
        Assert.Equal(ScoresheetMatchStatus.NotFound, ScoresheetNameMatcher.Match("", "כאהן", Roster).Status);
        Assert.Equal(ScoresheetMatchStatus.NotFound, ScoresheetNameMatcher.Match("אורי", "  ", Roster).Status);
    }
}
