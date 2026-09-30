using System.Net.Http.Headers;

namespace StatsHub.Api.IbbaScraping;

/// <summary>
/// ibasketball.co.il is served through two caches - Cloudflare (a separate
/// saved copy in every data center) and the site's own LiteSpeed page cache.
/// A cached schedule could still show a game's old date/time after IBBA
/// changed it: the production server (in the US) got a stale US copy and saw
/// "nothing changed", while a sync from Israel got fresh data. A unique query
/// parameter on every request makes both caches treat it as a new address and
/// fetch fresh from IBBA itself. WordPress ignores the unknown parameter.
/// Our traffic is small (a nightly sync plus manual syncs), so skipping the
/// cache doesn't load their site meaningfully.
/// </summary>
public class IbbaNoCacheHandler : DelegatingHandler
{
    public const string CacheBustParameter = "_sh";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && request.RequestUri is { } uri &&
            uri.Host.EndsWith("ibasketball.co.il", StringComparison.OrdinalIgnoreCase))
        {
            request.RequestUri = WithCacheBuster(uri);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            request.Headers.Pragma.ParseAdd("no-cache");
        }
        return base.SendAsync(request, cancellationToken);
    }

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
