using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace StatsHub.Api.IbbaScraping;

public class IbbaPlayerScraper
{
    private readonly HttpClient _http;

    public IbbaPlayerScraper(HttpClient http) => _http = http;

    /// <summary>
    /// Fetches a player profile page (e.g. https://ibasketball.co.il/player/xxxx-yyyy/)
    /// and extracts player name + every CURRENT team (main "קבוצה נוכחית" plus any
    /// "רשאי/ת" additional/loan team). Deliberately does NOT read "ליגות וקבוצות עבר"
    /// (past seasons/teams) - that section is out of scope, not current data.
    /// League URL/name per team is resolved separately by IbbaTeamScraper (from that
    /// team's own page), since רשאי teams don't get a league link here on the player page.
    /// </summary>
    public async Task<IbbaPlayerInfo> GetPlayerInfoAsync(string playerUrl)
    {
        var html = await _http.GetStringAsync(playerUrl);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var info = new IbbaPlayerInfo { PlayerUrl = playerUrl };

        // Player name sits in the <h1> on the profile page, e.g. <h1>עידן אלסטר</h1>
        var nameNode = doc.DocumentNode.SelectSingleNode("//h1");
        info.PlayerName = nameNode?.InnerText.Trim() ?? "";

        // IMPORTANT: the page's top nav menu also contains lots of /team/ and /league/
        // links (mega-menu of every league/club on the site) BEFORE the player's own
        // "קבוצה נוכחית" / "רשאי/ת" section further down. Taking the first /team/ link
        // on the page grabs a nav link instead of the player's real team - that was the
        // original bug. Instead we find the link whose *label text* matches, wherever it
        // sits on the page.

        var mainTeamNode = FindLabeledLink(doc, "קבוצה נוכחית", "/team/");
        if (mainTeamNode != null)
        {
            var rawHref = mainTeamNode.GetAttributeValue("href", "");
            var teamUrl = IbbaUrlHelper.Resolve(playerUrl, rawHref);
            info.Teams.Add(new IbbaPlayerTeamInfo
            {
                TeamUrl = teamUrl,
                TeamName = mainTeamNode.InnerText.Trim(),
                TeamSlugId = ExtractTeamSlugId(teamUrl),
            });
        }

        // רשאי/ת - additional team this player is also permitted to play for. Most
        // players (~90%) don't have one. Deliberately distinct from "ליגות וקבוצות עבר"
        // (past seasons) - StartsWith on the label text keeps these from being conflated.
        var additionalTeamNode = FindLabeledLink(doc, "רשאי", "/team/", allowFallback: false);
        if (additionalTeamNode != null)
        {
            var rawHref = additionalTeamNode.GetAttributeValue("href", "");
            var teamUrl = IbbaUrlHelper.Resolve(playerUrl, rawHref);
            // Guard against accidentally matching the same team twice (e.g. if the
            // labeled-link fallback strategy degrades to "first team link on page").
            if (!info.Teams.Any(t => t.TeamUrl == teamUrl))
            {
                info.Teams.Add(new IbbaPlayerTeamInfo
                {
                    TeamUrl = teamUrl,
                    TeamName = additionalTeamNode.InnerText.Trim(),
                    TeamSlugId = ExtractTeamSlugId(teamUrl),
                });
            }
        }

        return info;
    }

    /// <summary>
    /// Finds the &lt;a&gt; link (matching hrefContains) that belongs to a given label
    /// (e.g. "רשאי", "קבוצה נוכחית") on the page, without assuming a specific parent/child
    /// HTML relationship between the label text and the link - sites vary (label+link
    /// sharing one <li>, label in a <span> sibling, label in a preceding <dt>, etc.),
    /// and guessing the wrong shape silently falls through to "first link on page,"
    /// which is what caused the original league-1 bug.
    ///
    /// Strategy, most reliable first:
    ///  1) Find a text node whose trimmed content STARTS WITH the label, then take the
    ///     first matching &lt;a&gt; that follows it anywhere in document order (works
    ///     regardless of DOM nesting, since it doesn't rely on a shared parent).
    ///  2) Fallback: check up to 3 ancestor levels above each candidate link for text
    ///     starting with the label (covers the "shared parent" shape too).
    ///  3) Last resort: first link on the page matching hrefContains (old behavior) -
    ///     only used when allowFallback is true.
    /// </summary>
    private static HtmlNode? FindLabeledLink(HtmlDocument doc, string label, string hrefContains, bool allowFallback = true)
    {
        // Strategy 1: label as its own text node, then the nearest following matching <a>.
        var labelTextNodes = doc.DocumentNode.SelectNodes("//text()");
        if (labelTextNodes != null)
        {
            foreach (var textNode in labelTextNodes)
            {
                var text = HtmlEntity.DeEntitize(textNode.InnerText).Trim();
                if (text.Length > 0 && text.Length <= label.Length + 5 &&
                    text.StartsWith(label, StringComparison.Ordinal))
                {
                    var following = textNode.SelectSingleNode(
                        $"following::a[contains(@href,'{hrefContains}')][1]");
                    if (following != null)
                        return following;
                }
            }
        }

        // Strategy 2: check up to 3 ancestor levels above each candidate <a> for label text.
        var candidates = doc.DocumentNode.SelectNodes($"//a[contains(@href,'{hrefContains}')]");
        if (candidates != null)
        {
            foreach (var a in candidates)
            {
                var ancestor = a.ParentNode;
                for (int depth = 0; depth < 3 && ancestor != null; depth++, ancestor = ancestor.ParentNode)
                {
                    var ancestorText = HtmlEntity.DeEntitize(ancestor.InnerText).Trim();
                    if (ancestorText.StartsWith(label, StringComparison.Ordinal))
                        return a;
                }
            }
        }

        // Strategy 3: last resort, old "first link" behavior. Only used when the label is
        // expected to always be present (allowFallback: true) - e.g. "קבוצה נוכחית", which
        // virtually every player has. For an OPTIONAL label like "רשאי" (only ~10% of
        // players have one), this fallback must be skipped: most of the time it's genuinely
        // absent, and guessing "first /team/ link on the page" would fabricate a phantom
        // second team on every player who simply doesn't have one.
        if (!allowFallback) return null;

        return candidates?.FirstOrDefault();
    }

    /// <summary>
    /// Pulls the numeric id out of a team URL, e.g.
    /// https://ibasketball.co.il/team/10550-%d7%9e%d7%9b%d7%91%d7%99.../  -> "10550"
    /// This is the SLUG id (path id), NOT the internal export "team_id" used by ?feed=xlsx.
    /// Those are two different ids on this site - see IbbaTeamScraper for the export id.
    /// </summary>
    public static string ExtractTeamSlugId(string teamUrl)
    {
        var match = Regex.Match(teamUrl, @"/team/(\d+)-");
        return match.Success ? match.Groups[1].Value : "";
    }
}
