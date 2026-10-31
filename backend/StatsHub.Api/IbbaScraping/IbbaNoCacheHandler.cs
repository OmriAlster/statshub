using System.Net.Http.Headers;

namespace StatsHub.Api.IbbaScraping;

/// <summary>
/// ibasketball.co.il is served through two caches - Cloudflare (a separate
/// saved copy in every data center) and the site's own LiteSpeed page cache.
/// A cached schedule (the games spreadsheet) could still show a game's old
/// date/time after IBBA changed it: the production server (in the US) got a
/// stale copy and saw "nothing changed", while a sync from Israel got fresh
/// data.
///
/// The spreadsheet is therefore never taken from a cache older than a few
/// minutes: a parent waiting (linking, Sync Now) gets Cloudflare's copy when
/// it's at most MaxCopyAge old (~0.25 s, against 7-13 s for IBBA to build the
/// file), else a fresh one; the background jobs (nightly sync, results soon
/// after a game - see IbbaFreshness) always get a fresh one. A fresh copy is a
/// unique query parameter, which makes both caches treat it as a new address;
/// WordPress ignores it.
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
    public static readonly TimeSpan MaxCopyAge = TimeSpan.FromMinutes(10);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is not { } uri ||
            !uri.Host.EndsWith("ibasketball.co.il", StringComparison.OrdinalIgnoreCase) || !IsGamesSpreadsheet(uri))
            return await base.SendAsync(request, cancellationToken);

        if (!IbbaFreshness.FreshRequired)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (IsRecentEnough(response)) return response;
            response.Dispose();
            request = Copy(request); // a request message can only be sent once
        }

        request.RequestUri = WithCacheBuster(uri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        return await base.SendAsync(request, cancellationToken);
    }

    // A copy no older than MaxCopyAge - or one built just now for us (no
    // cache said it served a saved copy).
    internal static bool IsRecentEnough(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) return false;
        if (response.Headers.Age is { } age) return age <= MaxCopyAge;
        var fromCache = HeaderSays(response, "cf-cache-status", "HIT") || HeaderSays(response, "x-litespeed-cache", "hit");
        return !fromCache;
    }

    private static bool HeaderSays(HttpResponseMessage response, string name, string value) =>
        response.Headers.TryGetValues(name, out var values) && values.Any(v => v.Trim().Equals(value, StringComparison.OrdinalIgnoreCase));

    private static HttpRequestMessage Copy(HttpRequestMessage request)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in request.Options) ((IDictionary<string, object?>)copy.Options)[option.Key] = option.Value;
        return copy;
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

/// <summary>
/// Marks work no one is waiting on (the nightly sync, results soon after a
/// game), which always reads the games spreadsheet fresh rather than a copy
/// up to a few minutes old - see IbbaNoCacheHandler.
/// </summary>
public static class IbbaFreshness
{
    private static readonly AsyncLocal<bool> Fresh = new();

    public static bool FreshRequired => Fresh.Value;

    public static IDisposable RequireFresh()
    {
        var before = Fresh.Value;
        Fresh.Value = true;
        return new Restore(before);
    }

    private sealed class Restore(bool before) : IDisposable
    {
        public void Dispose() => Fresh.Value = before;
    }
}
