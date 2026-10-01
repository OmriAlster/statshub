using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StatsHub.Api.Data;
using StatsHub.Api.Services;

const string DevJwtKey = "dev-only-insecure-signing-key-change-me-please-32chars!";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<RequestTimings>();
builder.Services.AddSingleton<IbbaSyncTracker>();
builder.Services.AddTransient<StatsHub.Api.IbbaScraping.IbbaTimingHandler>();
RateLimits.Add(builder.Services, builder.Configuration);

// Behind Railway's proxy every request seems to come from the proxy - read
// the real address from X-Forwarded-For (the proxy's own entry only), so
// attempt limits are per person, not one shared bucket for everyone.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add CORS - origins come from localhost (dev) plus any production frontend
// URL(s) supplied via config/env (comma-separated for multiple domains, e.g.
// apex + www).
var configuredOrigins = (builder.Configuration["FrontendUrl"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var allowedOrigins = new[] { "http://localhost:5173", "http://localhost:3000" }
    .Concat(configuredOrigins)
    .Distinct()
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Any origin in dev - testing from a phone on the same WiFi means
            // a LAN IP that changes machine to machine and network to
            // network, so a fixed allow-list just breaks every time. Not a
            // concern outside Development, which is never publicly exposed.
            policy.SetIsOriginAllowed(_ => true)
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});

// Add Database Context. Railway (and most Postgres hosts) inject a
// DATABASE_URL in postgres://user:pass@host:port/db form; when present we
// use Postgres for production, otherwise fall back to the local SQLite file
// used in development.
var databaseUrl = builder.Configuration["DATABASE_URL"];
var usingPostgres = !string.IsNullOrEmpty(databaseUrl);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    // Round trips per request, in Server-Timing. Locally, optionally with a
    // production-like delay per query (Diagnostics:SimulatedDbLatencyMs).
    var simulatedDbLatencyMs = builder.Environment.IsDevelopment() ? builder.Configuration.GetValue("Diagnostics:SimulatedDbLatencyMs", 0) : 0;
    options.AddInterceptors(new TimingDbInterceptor(simulatedDbLatencyMs));
    if (usingPostgres)
    {
        options.UseNpgsql(ToNpgsqlConnectionString(databaseUrl!));
    }
    else
    {
        var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=statshub.db";
        options.UseSqlite(connectionString);
    }
});

// JWT authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? DevJwtKey;
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "StatsHub";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "StatsHubClient";

if (!builder.Environment.IsDevelopment() && jwtKey == DevJwtKey)
{
    throw new InvalidOperationException(
        "Refusing to start outside Development with the default JWT signing key. Set the Jwt:Key configuration value (e.g. the Jwt__Key environment variable) to a strong, unique secret.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };

    // A token stops working once its account's security stamp changes
    // ("Log out of all devices") - checked against the database, cached for
    // 30 seconds so it isn't a query on every request.
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                context.Fail("Invalid token.");
                return;
            }
            var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var currentStamp = await cache.GetOrCreateAsync(SessionStamp.CacheKey(userId), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var user = await db.Users.Where(u => u.Id == userId).Select(u => new { u.SecurityStamp }).FirstOrDefaultAsync();
                return user == null ? null : user.SecurityStamp ?? string.Empty;
            });
            var tokenStamp = context.Principal?.FindFirstValue(SessionStamp.ClaimType);
            if (currentStamp == null || (currentStamp != string.Empty && currentStamp != tokenStamp))
                context.Fail("This session has ended - please sign in again.");
        },
    };
});

builder.Services.AddAuthorization();

// Add Services (Dependency Injection)
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPlayerService, PlayerService>();
builder.Services.AddScoped<ISeasonService, SeasonService>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<IGameService, GameService>();
builder.Services.AddScoped<IGameStatsService, GameStatsService>();
builder.Services.AddScoped<IShotService, ShotService>();
builder.Services.AddScoped<IShareService, ShareService>();
builder.Services.AddScoped<IIbbaService, IbbaService>();
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
builder.Services.AddHostedService<GameReminderBackgroundService>();
builder.Services.AddHostedService<IbbaNightlySyncBackgroundService>();
builder.Services.AddHttpClient("Ibba", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36");
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.All
})
// Times each IBBA page for the request's Server-Timing - see IbbaTimingHandler.
.AddHttpMessageHandler<StatsHub.Api.IbbaScraping.IbbaTimingHandler>()
// Always fetch fresh from IBBA, never a cached copy - see IbbaNoCacheHandler.
.AddHttpMessageHandler(() => new StatsHub.Api.IbbaScraping.IbbaNoCacheHandler());

var app = builder.Build();

// Ensure the database and schema exist. EnsureCreated() builds the full
// current schema on a brand-new database - all a fresh deploy needs - but is a
// complete no-op on one that already exists, on any provider. So every model
// change made since first deploy needs an explicit, idempotent upgrader:
// SchemaUpgrader for the local SQLite file, PostgresSchemaUpgrader for
// production. Both are safe to run on every startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // A database from before UUID ids: bring its (numeric-id) schema up to
    // date with the old upgraders, then convert every id to a UUID - once.
    // A database created since (or already converted) has the current schema
    // from EnsureCreated, so the old upgraders - written for numeric ids -
    // never run on it. A schema change from now on needs its own idempotent
    // step here for UUID databases (EnsureCreated never alters an existing one).
    if (UuidMigration.IsLegacy(db))
    {
        if (usingPostgres) PostgresSchemaUpgrader.Apply(db);
        else SchemaUpgrader.Apply(db);
        UuidMigration.Run(db, scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("UuidMigration"));
    }
}

// Reports how long the server itself spent on each request, as a standard
// Server-Timing response header (visible in the browser's network tab) - so
// slowness can be split into "the server was slow" vs "the network was slow"
// without shell access to the host.
// The games-spreadsheet reader's one-time setup, off the first sync's path.
_ = Task.Run(() =>
{
    try { StatsHub.Api.IbbaScraping.IbbaTeamScraper.WarmUpSpreadsheetReader(); }
    catch (Exception ex) { app.Logger.LogWarning(ex, "Spreadsheet reader warm-up failed"); }
});

// Requests that did slow work (IBBA pages, sync phases) list each part too,
// and are logged with the breakdown - see RequestTimings.
app.Use(async (context, next) =>
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var timings = context.RequestServices.GetRequiredService<RequestTimings>();
    RequestTimings.Current = timings;
    context.Response.OnStarting(() =>
    {
        var header = $"app;dur={stopwatch.Elapsed.TotalMilliseconds:F1}";
        if (timings.Any) header += ", " + timings.ToServerTimingHeader();
        context.Response.Headers["Server-Timing"] = header;
        return Task.CompletedTask;
    });
    await next();
    if (timings.Any)
    {
        app.Logger.LogInformation("{Method} {Path} took {Total:F0}ms: {Breakdown}",
            context.Request.Method, context.Request.Path, stopwatch.Elapsed.TotalMilliseconds, timings);
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders();
    app.UseHsts();
}

// Standard hardening headers on every response. The API only returns JSON,
// so it never needs to be framed, sniffed as another type, or send a referrer.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    await next();
});

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseRateLimiter(); // after authentication, so limits are per signed-in account
app.UseAuthorization();

app.MapControllers();

app.Run();

// Converts a postgres://user:pass@host:port/db URL (the form Railway and
// most other hosts inject) into the key=value connection string Npgsql
// expects.
static string ToNpgsqlConnectionString(string databaseUrl)
{
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':', 2);
    var builder = new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = uri.AbsolutePath.TrimStart('/'),
        Username = Uri.UnescapeDataString(userInfo[0]),
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
        SslMode = Npgsql.SslMode.Require,
        TrustServerCertificate = true,
        // Opening a brand-new database connection costs ~850ms in production
        // (several network round trips for TCP + TLS + auth), and idle pooled
        // connections get closed after 5 minutes - so a family opening the
        // app after a quiet stretch paid that on every parallel request of
        // the first page load. Keep a few open at all times instead, with a
        // periodic keepalive so the network path doesn't silently drop them.
        MinPoolSize = 5,
        KeepAlive = 30,
    };
    return builder.ConnectionString;
}

// Lets the integration tests (StatsHub.Api.Tests) start this exact app with
// WebApplicationFactory<Program> - top-level statements otherwise make the
// generated Program class internal.
public partial class Program { }
