using System.Diagnostics;
using StatsHub.Api.Services;

namespace StatsHub.Api.IbbaScraping;

// Times every request to the IBBA site (until the whole page is in hand) and
// records it on the current RequestTimings, labelled by kind of page - so a
// slow link/sync, or a background job, shows exactly which IBBA pages it
// waited on.
public class IbbaTimingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var timings = RequestTimings.Current;
        if (timings == null) return await base.SendAsync(request, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var response = await base.SendAsync(request, cancellationToken);
        await response.Content.LoadIntoBufferAsync(cancellationToken);
        timings.Add(Label(request.RequestUri), stopwatch.Elapsed.TotalMilliseconds);
        return response;
    }

    private static string Label(Uri? uri)
    {
        var url = uri?.ToString() ?? "";
        if (url.Contains("feed=xlsx")) return "ibba-games-xlsx";
        var path = uri?.AbsolutePath ?? "";
        if (path.StartsWith("/player/")) return "ibba-player-page";
        if (path.StartsWith("/team/")) return "ibba-team-page";
        if (path.StartsWith("/league/")) return "ibba-league-page";
        return "ibba-other-page";
    }
}
