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
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"statshub-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // dev-login is the test sign-in path
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
        builder.UseSetting("DATABASE_URL", ""); // never Postgres, even if the machine has one set
        builder.UseSetting("Logging:LogLevel:Default", "Warning"); // SQL-per-query logging is just noise here

        builder.ConfigureTestServices(services =>
        {
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}
