using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace StatsHub.Api.Services
{
    // How many requests one person can make. Sign-in, registration and
    // invite-code claims are kept low so passwords and codes can't be guessed
    // by trying many; everything else has a generous ceiling that a family
    // tracking a live game never gets near. Configurable (RateLimits:*) so
    // the tests, which sign in many times from one address, aren't throttled.
    public static class RateLimits
    {
        public const string SignIn = "sign-in";

        public static void Add(IServiceCollection services, IConfiguration configuration)
        {
            var signInPerMinute = configuration.GetValue("RateLimits:SignInPerMinute", 10);
            var requestsPerMinute = configuration.GetValue("RateLimits:RequestsPerMinute", 600);

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                        context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new { message = "Too many attempts - wait a minute and try again." }, cancellationToken);
                };

                // Per signed-in account, or per address when signed out.
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        http.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId ? $"user:{userId}" : $"ip:{ClientAddress(http)}",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = requestsPerMinute, Window = TimeSpan.FromMinutes(1) }));

                options.AddPolicy(SignIn, http =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        ClientAddress(http),
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = signInPerMinute, Window = TimeSpan.FromMinutes(1) }));
            });
        }

        private static string ClientAddress(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
