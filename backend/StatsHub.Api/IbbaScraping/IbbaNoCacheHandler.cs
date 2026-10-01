using System.Net.Http.Headers;

namespace StatsHub.Api.IbbaScraping;

/// <summary>
/// ibasketball.co.il is served through two caches - Cloudflare (a separate
/// saved copy in every data center) and the site's own LiteSpeed page cache.
/// A cached schedule (the games spreadsheet) could still show a game's old
/// date/time after IBBA changed it: the production server (in the US) got a
/// stale copy and saw "nothing changed", while a sync from Israel got fresh
/// data. So the spreadsheet is always fetched fresh - a unique query parameter
/// makes both caches treat it as a new address. WordPress ignores it.
///
/// Only the spreadsheet, though: IBBA takes 15-25 seconds to build any page
/// itself, against ~0.3s for a cached copy, and skipping the cache for every
/// page made one IBBA link take minutes. The player, team and league pages
/// (team list, crest, standings) aren't where game times live, and IBBA's
/// page cache is refreshed when that content changes.
/// </summary>
public class IbbaNoCacheHandler : DelegatingHandler
{
    public const string CacheBustParameter = "_sh";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && request.RequestUri is { } uri &&
            uri.Host.EndsWith("ibasketball.co.il", StringComparison.OrdinalIgnoreCase) &&
            IsGamesSpreadsheet(uri))
        {
            request.RequestUri = WithCacheBuster(uri);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            request.Headers.Pragma.ParseAdd("no-cache");
        }
        return base.SendAsync(request, cancellationToken);
    }

    public static bool IsGamesSpreadsheet(Uri uri) => uri.Query.Contains("feed=xlsx", StringComparison.OrdinalIgnoreCase);

    public static Uri WithCacheBuster(Uri uri)
    {
        var builder = new UriBuilder(uri);
        var bust = $"{CacheBustParameter}={DateTime.UtcNow.Ticks}";
        var existing = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(existing) ? bust : $"{existing}&{bust}";
        return builder.Uri;
    }

    // For addresses we keep (a team's canonical page after a redirect), so
    // the cache-buster never ends up stored in the database.
    public static string WithoutQuery(Uri uri) => uri.GetLeftPart(UriPartial.Path);
}
