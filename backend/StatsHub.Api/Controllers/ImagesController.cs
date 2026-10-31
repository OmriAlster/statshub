using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace StatsHub.Api.Controllers
{
    // IBBA's crests and player photos, passed through - IBBA sends them
    // without CORS headers, so a page can show them but not draw them into
    // an image it makes (the player card, the season summary). Only IBBA's
    // own uploads are fetched.
    [ApiController]
    [Route("api/images")]
    [Authorize]
    public class ImagesController : ControllerBase
    {
        private const int MaxBytes = 3 * 1024 * 1024;
        private readonly IHttpClientFactory _http;
        private readonly IMemoryCache _cache;

        public ImagesController(IHttpClientFactory http, IMemoryCache cache)
        {
            _http = http;
            _cache = cache;
        }

        internal static bool Allowed(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host == "ibasketball.co.il"
            && uri.IsDefaultPort
            && uri.AbsolutePath.StartsWith("/wp-content/uploads/", StringComparison.Ordinal);

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string url)
        {
            if (!Allowed(url)) return BadRequest(new { message = "Only IBBA images" });

            var image = await _cache.GetOrCreateAsync($"image:{url}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                entry.Size = 1;
                try
                {
                    using var response = await _http.CreateClient().GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    var type = response.Content.Headers.ContentType?.MediaType;
                    if (!response.IsSuccessStatusCode || type == null || !type.StartsWith("image/", StringComparison.Ordinal)) return null;
                    if (response.Content.Headers.ContentLength > MaxBytes) return null;
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    return bytes.Length > MaxBytes ? null : new CachedImage(bytes, type);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    return null;
                }
            });
            if (image == null) return NotFound();
            Response.Headers.CacheControl = "private, max-age=86400";
            return File(image.Bytes, image.Type);
        }

        private sealed record CachedImage(byte[] Bytes, string Type);
    }
}
