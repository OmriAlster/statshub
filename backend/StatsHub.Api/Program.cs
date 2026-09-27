using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
builder.Services.AddHttpClient("Ibba", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36");
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.All
});

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
    if (usingPostgres)
    {
        PostgresSchemaUpgrader.Apply(db);
    }
    else
    {
        SchemaUpgrader.Apply(db);
    }
}

// Reports how long the server itself spent on each request, as a standard
// Server-Timing response header (visible in the browser's network tab) - so
// slowness can be split into "the server was slow" vs "the network was slow"
// without shell access to the host.
app.Use(async (context, next) =>
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["Server-Timing"] = $"app;dur={stopwatch.Elapsed.TotalMilliseconds:F1}";
        return Task.CompletedTask;
    });
    await next();
});

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// TEMPORARY latency probe - measures the database round trip from inside
// the server (DNS lookup, opening a brand-new connection, and a trivial
// query on a pooled one), to find why every query takes ~150ms in
// production vs ~1ms locally. Returns only timings - no hostnames,
// credentials, or data. Remove once the cause is found.
app.MapGet("/api/diag/latency", async (AppDbContext db) =>
{
    if (!usingPostgres) return Results.NotFound();

    var csb = new Npgsql.NpgsqlConnectionStringBuilder(ToNpgsqlConnectionString(databaseUrl!));
    var sw = System.Diagnostics.Stopwatch.StartNew();

    var dnsStart = sw.Elapsed;
    var addresses = await System.Net.Dns.GetHostAddressesAsync(csb.Host!);
    var dnsMs = (sw.Elapsed - dnsStart).TotalMilliseconds;

    // A brand-new physical connection (pooling off): TCP + TLS + auth.
    csb.Pooling = false;
    var openStart = sw.Elapsed;
    await using (var fresh = new Npgsql.NpgsqlConnection(csb.ConnectionString))
    {
        await fresh.OpenAsync();
    }
    var freshOpenMs = (sw.Elapsed - openStart).TotalMilliseconds;

    // The app's own pooled connection, same path every request uses.
    var conn = db.Database.GetDbConnection();
    var pooledOpenStart = sw.Elapsed;
    await conn.OpenAsync();
    var pooledOpenMs = (sw.Elapsed - pooledOpenStart).TotalMilliseconds;

    var selectMs = new List<double>();
    for (var i = 0; i < 5; i++)
    {
        var start = sw.Elapsed;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        await cmd.ExecuteScalarAsync();
        selectMs.Add(Math.Round((sw.Elapsed - start).TotalMilliseconds, 1));
    }
    await conn.CloseAsync();

    return Results.Ok(new
    {
        dnsMs = Math.Round(dnsMs, 1),
        addressFamilies = addresses.Select(a => a.AddressFamily.ToString()).Distinct(),
        freshConnectionOpenMs = Math.Round(freshOpenMs, 1),
        pooledConnectionOpenMs = Math.Round(pooledOpenMs, 1),
        selectOneMs = selectMs,
        totalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1),
    });
});

app.UseCors("AllowFrontend");

app.UseAuthentication();
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
    };
    return builder.ConnectionString;
}
