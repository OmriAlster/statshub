using System.Globalization;
using System.Text;

namespace StatsHub.Api.Scoresheets;

public enum ScoresheetMatchStatus
{
    /// <summary>Exactly one roster player fits.</summary>
    Matched,
    /// <summary>Nobody on the roster fits - the player is not on that sheet.</summary>
    NotFound,
    /// <summary>More than one fits (e.g. two "Cohen, N." on one team) - the user must choose.</summary>
    Ambiguous,
}

public sealed record ScoresheetMatch(ScoresheetMatchStatus Status, ScoresheetPlayer? Player, IReadOnlyList<ScoresheetPlayer> Candidates);

/// <summary>
/// Finds a StatsHub player on a scoresheet roster. The sheet only has the last name and the
/// first-name initial ("כאהן, א."), so a match is last name + initial within ONE team's roster.
/// Once matched, the caller should remember <see cref="ScoresheetPlayer.Code"/> and use it from
/// then on - the name is only for the first link.
/// </summary>
public static class ScoresheetNameMatcher
{
    public static ScoresheetMatch Match(string firstName, string lastName, IReadOnlyList<ScoresheetPlayer> roster)
    {
        string wantedLast = NormalizeLastName(lastName);
        string wantedInitial = FirstLetter(firstName);
        if (wantedLast.Length == 0 || wantedInitial.Length == 0)
            return new ScoresheetMatch(ScoresheetMatchStatus.NotFound, null, Array.Empty<ScoresheetPlayer>());

        var candidates = roster
            .Where(p => NormalizeLastName(p.LastName) == wantedLast && FirstLetter(p.FirstInitial) == wantedInitial)
            .ToList();

        return candidates.Count switch
        {
            0 => new ScoresheetMatch(ScoresheetMatchStatus.NotFound, null, candidates),
            1 => new ScoresheetMatch(ScoresheetMatchStatus.Matched, candidates[0], candidates),
            _ => new ScoresheetMatch(ScoresheetMatchStatus.Ambiguous, null, candidates),
        };
    }

    /// <summary>
    /// Lower-cases, drops niqqud, quotes, punctuation, spaces/hyphens and any "(x)" qualifier,
    /// so "בית"ר" / "ביתר", "ברש אור" / "ברש-אור" and "בראונר (ח)" / "בראונר" compare equal.
    /// </summary>
    public static string NormalizeLastName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sb = new StringBuilder();
        int depth = 0;
        foreach (char ch in value)
        {
            if (ch == '(') { depth++; continue; }
            if (ch == ')') { if (depth > 0) depth--; continue; }
            if (depth > 0) continue;
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (!char.IsLetterOrDigit(ch)) continue; // spaces, quotes (" ' ״ ׳), hyphens, dots, commas
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static string FirstLetter(string? value)
    {
        string normalized = NormalizeLastName(value);
        return normalized.Length == 0 ? string.Empty : normalized[..1];
    }
}
