using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace StatsHub.Api.Data
{
    // Brings the database to the current schema at startup, with EF Core
    // migrations (Data/Migrations/Sqlite and /Postgres - add one with
    // `dotnet ef migrations add <Name> --context SqliteAppDbContext
    // --output-dir Data/Migrations/Sqlite`, and the same for
    // PostgresAppDbContext into Data/Migrations/Postgres).
    //
    // Databases from before migrations were made by EnsureCreated (plus the
    // old upgraders), so they have every table but no migration history.
    // Their schema is exactly the first migration's (InitialCreate) - the
    // UUID conversion rebuilt every table from the model, and the model
    // hasn't changed since - so that migration is recorded as already
    // applied, without touching a table, and any later ones run normally.
    public static class DatabaseSetup
    {
        public const string Baseline = "InitialCreate";

        public static void Apply(AppDbContext db, ILogger logger)
        {
            var creator = db.GetService<IRelationalDatabaseCreator>();
            var history = db.GetService<IHistoryRepository>();

            if (creator.Exists() && creator.HasTables() && !history.Exists())
            {
                var migrations = db.Database.GetMigrations().ToList();
                var applied = new List<string>();
                if (UuidMigration.IsLegacy(db))
                {
                    // A numeric-id database: the old upgraders, then the UUID
                    // conversion, which builds every table from today's model -
                    // so every migration is already in it.
                    if (db.Database.IsNpgsql()) PostgresSchemaUpgrader.Apply(db);
                    else SchemaUpgrader.Apply(db);
                    UuidMigration.Run(db, logger);
                    applied.AddRange(migrations);
                }
                else
                {
                    applied.Add(migrations.Single(m => m.EndsWith("_" + Baseline)));
                }

                db.Database.ExecuteSqlRaw(history.GetCreateIfNotExistsScript());
                foreach (var migration in applied)
                    db.Database.ExecuteSqlRaw(history.GetInsertScript(new HistoryRow(migration, ProductInfo.GetVersion())));
                logger.LogInformation("Database from before migrations: recorded {Migrations} as already applied", string.Join(", ", applied));
            }

            db.Database.Migrate();
        }
    }
}
