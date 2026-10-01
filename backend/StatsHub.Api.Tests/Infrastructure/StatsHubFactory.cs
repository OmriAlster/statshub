using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StatsHub.Api.Data;
using StatsHub.Api.Services;

namespace StatsHub.Api.Tests.Infrastructure;

// Boots the real API (same Program.cs, same services, same schema setup)
// against its own throwaway SQLite file. Background jobs are removed - the
// nightly IBBA sync would scrape the real site, and the reminder sweep would
// race the tests over the same rows.
public class StatsHubFactory : WebApplicationFactory<Program>
{
    // Set before first use to test the sign-in attempt limit itself.
    public string? RateLimitOverride { get; init; }

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"statshub-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // dev-login is the test sign-in path
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
        builder.UseSetting("DATABASE_URL", ""); // never Postgres, even if the machine has one set
        builder.UseSetting("Logging:LogLevel:Default", "Warning"); // SQL-per-query logging is just noise here
        // Every test signs in from the same address, many times - attempt
        // limits are tested on their own (SecurityTests) with a fresh factory.
        builder.UseSetting("RateLimits:SignInPerMinute", RateLimitOverride ?? "100000");
        builder.UseSetting("RateLimits:RequestsPerMinute", "100000");

        builder.ConfigureTestServices(services =>
        {
            // Nothing in the tests may ever reach the real ibasketball.co.il -
            // every IBBA request gets "not found" (a sync then just records a
            // sync error, like a real site outage).
            services.AddHttpClient("Ibba").ConfigurePrimaryHttpMessageHandler(() => new OfflineIbbaSite());

            foreach (var jobType in new[] { typeof(GameReminderBackgroundService), typeof(IbbaNightlySyncBackgroundService) })
            {
                var registration = services.FirstOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == jobType);
                if (registration != null) services.Remove(registration);
            }
        });
    }

    // Direct database access for setting up states the public API can't
    // create on its own (e.g. IBBA-linked teams without scraping the site).
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
    }

    private sealed class OfflineIbbaSite : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { RequestMessage = request });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Only THIS database's pooled connections - not ClearAllPools(), which
        // also closes the connections of other test classes still running in
        // parallel against their own databases (a flaky "disposed object").
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        }
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}
