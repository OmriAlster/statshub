using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StatsHub.Api.Data
{
    // The same model on each database, each with its own migrations
    // (Data/Migrations/Sqlite, Data/Migrations/Postgres). Everything else in
    // the app asks for AppDbContext; Program.cs registers whichever one the
    // environment uses.
    public class SqliteAppDbContext : AppDbContext
    {
        public SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : base(options) { }
    }

    public class PostgresAppDbContext : AppDbContext
    {
        public PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options) : base(options) { }
    }

    // For `dotnet ef migrations add` - it never connects, it only needs the provider.
    public class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
    {
        public SqliteAppDbContext CreateDbContext(string[] args) =>
            new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("Data Source=design-time.db").Options);
    }

    public class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
    {
        public PostgresAppDbContext CreateDbContext(string[] args) =>
            new(new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql("Host=localhost;Database=design_time").Options);
    }
}
