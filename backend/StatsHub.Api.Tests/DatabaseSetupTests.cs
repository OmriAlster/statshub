using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsHub.Api.Data;

namespace StatsHub.Api.Tests;

// The move to migrations. Every database in use today (production, local)
// was built by EnsureCreated - CreateTables from the model - and has no
// migration history. The first migration must build exactly that schema, so
// marking it as applied there is safe; and doing so must not touch the data.
public abstract class DatabaseSetupTestsBase
{
    protected abstract AppDbContext NewContext(string database);
    protected abstract List<string> Schema(AppDbContext db);

    protected static List<string> Query(AppDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "null" : reader.GetValue(i).ToString())));
        return rows;
    }

    private static List<string> AppliedMigrations(AppDbContext db) => db.Database.GetAppliedMigrations().ToList();

    protected void TheFirstMigrationBuildsTheSameSchemaAsToday()
    {
        using var existing = NewContext("existing");
        existing.GetService<IRelationalDatabaseCreator>().CreateTables();
        using var migrated = NewContext("migrated");
        migrated.Database.Migrate();

        Assert.Equal(Schema(existing), Schema(migrated));
    }

    protected void ADatabaseFromBeforeMigrationsIsMarkedNotRebuilt()
    {
        using (var db = NewContext("existing"))
        {
            db.GetService<IRelationalDatabaseCreator>().CreateTables();
            db.Users.Add(new Models.User { Email = "parent@example.test", FirstName = "Dana", LastName = "Levi" });
            db.SaveChanges();
        }

        using (var db = NewContext("existing"))
        {
            var creator = db.GetService<IRelationalDatabaseCreator>();
            Assert.True(creator.Exists(), "database exists");
            Assert.True(creator.HasTables(), "database has tables");
            DatabaseSetup.Apply(db, NullLogger.Instance);
            Assert.Equal(db.Database.GetMigrations(), AppliedMigrations(db));
            Assert.Equal("parent@example.test", db.Users.Single().Email);
        }

        using (var db = NewContext("existing"))
            DatabaseSetup.Apply(db, NullLogger.Instance); // every later startup: nothing to do
    }

    protected void ANewDatabaseIsBuiltByTheMigrations()
    {
        using var db = NewContext("new");
        DatabaseSetup.Apply(db, NullLogger.Instance);

        Assert.Equal(db.Database.GetMigrations(), AppliedMigrations(db));
        db.Users.Add(new Models.User { Email = "new@example.test" });
        db.SaveChanges();
    }
}

public sealed class SqliteDatabaseSetupTests : DatabaseSetupTestsBase, IDisposable
{
    private readonly string _prefix = Path.Combine(Path.GetTempPath(), $"statshub-setup-{Guid.NewGuid():N}");

    protected override AppDbContext NewContext(string database) =>
        new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite($"Data Source={_prefix}-{database}.db").Options);

    // Every table and index, as SQLite stores its definition.
    protected override List<string> Schema(AppDbContext db) =>
        Query(db, "SELECT type, name, sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EFMigrations%' ORDER BY type, name");

    [Fact] public void The_first_migration_builds_the_same_schema_as_today() => TheFirstMigrationBuildsTheSameSchemaAsToday();
    [Fact] public void A_database_from_before_migrations_is_marked_not_rebuilt() => ADatabaseFromBeforeMigrationsIsMarkedNotRebuilt();
    [Fact] public void A_new_database_is_built_by_the_migrations() => ANewDatabaseIsBuiltByTheMigrations();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_prefix)!, Path.GetFileName(_prefix) + "*"))
            File.Delete(file);
    }
}

// Production's database. Runs where STATSHUB_TEST_POSTGRES is set (CI).
public sealed class PostgresDatabaseSetupTests : DatabaseSetupTestsBase, IDisposable
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("STATSHUB_TEST_POSTGRES");
    private readonly string _prefix = $"statshub_setup_{Guid.NewGuid():N}";
    private readonly HashSet<string> _created = new();

    protected override AppDbContext NewContext(string database)
    {
        var name = $"{_prefix}_{database}";
        if (_created.Add(name))
        {
            using var admin = new NpgsqlConnection(Server);
            admin.Open();
            using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            command.ExecuteNonQuery();
        }
        return new PostgresAppDbContext(new DbContextOptionsBuilder<PostgresAppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(Server) { Database = name }.ConnectionString).Options);
    }

    // Columns (type, nullability, default), indexes and constraints.
    protected override List<string> Schema(AppDbContext db) =>
        Query(db, """
            SELECT table_name, column_name, data_type, is_nullable, coalesce(column_default, '') FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name NOT LIKE '__EFMigrations%' ORDER BY table_name, column_name
            """)
        .Concat(Query(db, "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename NOT LIKE '__EFMigrations%' ORDER BY indexname"))
        .Concat(Query(db, """
            SELECT conname, pg_get_constraintdef(c.oid) FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
            WHERE n.nspname = 'public' AND conname NOT LIKE '%EFMigrations%' ORDER BY conname
            """))
        .ToList();

    [PostgresFact] public void The_first_migration_builds_the_same_schema_as_today() => TheFirstMigrationBuildsTheSameSchemaAsToday();
    [PostgresFact] public void A_database_from_before_migrations_is_marked_not_rebuilt() => ADatabaseFromBeforeMigrationsIsMarkedNotRebuilt();
    [PostgresFact] public void A_new_database_is_built_by_the_migrations() => ANewDatabaseIsBuiltByTheMigrations();

    public void Dispose()
    {
        if (Server == null) return;
        NpgsqlConnection.ClearAllPools();
        using var admin = new NpgsqlConnection(Server);
        admin.Open();
        foreach (var name in _created)
        {
            using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
            command.ExecuteNonQuery();
        }
    }
}
