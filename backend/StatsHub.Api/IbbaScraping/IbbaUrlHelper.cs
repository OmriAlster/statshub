namespace StatsHub.Api.IbbaScraping;

/// <summary>
/// Scraped hrefs on ibasketball.co.il aren't always absolute - the Excel export link in
/// particular is often just "?feed=xlsx&team_id=..." (relative to the current page).
/// This resolves any href (absolute, root-relative "/team/...", or query-only
/// "?feed=...") against the page URL it was scraped from.
/// </summary>
public static class IbbaUrlHelper
{
    public static string Resolve(string pageUrl, string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return href;

        // Already absolute (http:// or https://) -> use as-is
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        // Relative (root-relative "/x", query-only "?x", or "x/y") -> resolve against page
        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri) &&
            Uri.TryCreate(baseUri, href, out var resolved))
        {
            return resolved.ToString();
        }

        // Fallback: return whatever we got rather than throwing here;
        // the HttpClient call downstream will surface a clear error if it's still bad.
        return href;
    }
}
