using System.Globalization;
using System.Text;

namespace StatsHub.Api.Scoresheets;

// Finding IBBA's teams, leagues and players in Swish, which spells them its own way.
public static class SwishMatching
{
    // Letters and digits only - "בית"ר" / "ביתר", "גזר/נען" / "גזר נען" compare equal.
    public static string Norm(string? s)
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
    public static bool SameTeam(string swishName, string ibbaName)
    {
        string a = Norm(swishName), b = Norm(ibbaName);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;
        return Math.Min(a.Length, b.Length) >= 10 && (a.StartsWith(b) || b.StartsWith(a));
    }

    // The sheet only has a last name and an initial; the IBBA name is "first ... last", where
    // either part can be more than one word - every split is tried, and one player must fit.
    public static (ScoresheetMatchStatus Status, ScoresheetPlayer? Player, List<ScoresheetPlayer> Candidates) FindPlayer(
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

    // The age group a league is for, by its name: "נערים א מחוזית שרון" is נערים א,
    // "ארצית דרום" (no age group) is the men's. A cup is for one age group - "גביע האיגוד
    // לנערים א" - so a team plays in the cups of its league's age group only.
    private static readonly string[] AgeGroups = { "נערות א", "נערות ב", "נערים א", "נערים ב", "ילדות", "ילדים", "נוער", "קט סל" };

    public static string AgeGroupOf(string leagueName)
    {
        foreach (var group in AgeGroups)
            if (Norm(leagueName).StartsWith(Norm(group))) return group;
        return Norm(leagueName).Contains(Norm("נשים")) ? "נשים" : "גברים";
    }

    public static bool IsCup(SwishCompetition c) => c.Name.Contains("גביע");

    public static bool CupFor(SwishCompetition cup, string ageGroup) => Norm(cup.Name).Contains(Norm("ל" + ageGroup));

    // Where a team of this IBBA league plays in Swish: the league itself (by name - null when
    // Swish doesn't have it) and its age group's cups.
    public static (SwishCompetition? League, List<SwishCompetition> Cups) CompetitionsFor(IEnumerable<SwishCompetition> all, string leagueName)
    {
        var list = all.ToList();
        var league = list.FirstOrDefault(c => !IsCup(c) && Norm(c.Name) == Norm(leagueName));
        var ageGroup = AgeGroupOf(leagueName);
        return (league, list.Where(c => IsCup(c) && CupFor(c, ageGroup)).ToList());
    }
}
