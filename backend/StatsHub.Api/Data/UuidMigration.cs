using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace StatsHub.Api.Data
{
    // One-time move from numeric ids (1, 2, 3...) to UUIDs for every table.
    //
    // Runs at startup on a database that still has numeric ids, after the old
    // SchemaUpgrader/PostgresSchemaUpgrader brought its columns up to date:
    // every row is read into memory, the tables are recreated from the
    // current model (UUID keys), and the rows are written back with each id -
    // and every column pointing at one - swapped for its new UUID. All in one
    // transaction: it either completes or leaves the database as it was.
    //
    // The old tables aren't thrown away: on Postgres (production) they're
    // moved, rows and all, into a separate "pre_uuid_backup" schema in the
    // same database - a copy to go back to, since the hosting plan has no
    // backups; drop that schema once the converted data is trusted. On SQLite
    // the whole file is copied first instead (SQLite has no schemas). New UUIDs are time-ordered (version 7) and handed out in the old
    // id order, so "oldest first" still holds for converted rows.
    public static class UuidMigration
    {
        // Every table, parents before children, with the columns that point
        // at another table's id. Games' home/away columns point at IbbaTeams
        // for a synced game (IbbaGameCode set) and at Teams for a manual one.
        private const string HomeAwayTeam = "*home-away*";
        private static readonly (string Table, Dictionary<string, string> Refs)[] Tables =
        {
            ("Users", new()),
            ("IbbaTeams", new()),
            ("Players", new() { ["UserId"] = "Users", ["LinkedUserId"] = "Users" }),
            ("PlayerParents", new() { ["PlayerId"] = "Players", ["UserId"] = "Users" }),
            ("Seasons", new() { ["UserId"] = "Users" }),
            ("Teams", new() { ["SeasonId"] = "Seasons", ["IbbaTeamId"] = "IbbaTeams" }),
            ("PlayerTeams", new() { ["PlayerId"] = "Players", ["TeamId"] = "Teams" }),
            ("Games", new() { ["LiveTrackedByUserId"] = "Users", ["HomeTeamId"] = HomeAwayTeam, ["AwayTeamId"] = HomeAwayTeam }),
            ("GameStats", new() { ["GameId"] = "Games", ["PlayerId"] = "Players" }),
            ("Shots", new() { ["GameStatsId"] = "GameStats" }),
            ("ShareLinks", new() { ["PlayerId"] = "Players", ["GameId"] = "Games", ["CreatedByUserId"] = "Users" }),
            ("PlayerIbbaLinks", new() { ["PlayerId"] = "Players" }),
            ("PlayerIbbaTeams", new() { ["PlayerIbbaLinkId"] = "PlayerIbbaLinks", ["IbbaTeamId"] = "IbbaTeams" }),
            ("PushSubscriptions", new() { ["UserId"] = "Users" }),
        };

        // For the tests, which build an old-style database in this order.
        internal static IReadOnlyList<string> TableOrder => Tables.Select(t => t.Table).ToList();

        public const string BackupSchema = "pre_uuid_backup";

        // Tables from older versions that nothing uses any more.
        private static readonly string[] Leftovers = { "IbbaTeamLinks", "IbbaStandings", "IbbaTeamCrests" };

        // Converted ids are spread from here, a millisecond apart in old-id
        // order - always before any id made after the conversion.
        private static readonly DateTimeOffset Origin = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static bool IsLegacy(AppDbContext db)
        {
            var sql = db.Database.IsNpgsql()
                ? "SELECT data_type FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'Users' AND column_name = 'Id'"
                : "SELECT type FROM pragma_table_info('Users') WHERE name = 'Id'";
            var type = Scalar(db, sql)?.ToString()?.ToLowerInvariant();
            return type is "integer" or "bigint";
        }

        public static void Run(AppDbContext db, ILogger logger)
        {
            var postgres = db.Database.IsNpgsql();
            CheckEveryTableIsCovered(db);

            if (!postgres) BackUpSqliteFile(db, logger);

            using var transaction = db.Database.BeginTransaction();
            var tx = transaction.GetDbTransaction();
            var connection = db.Database.GetDbConnection();

            // 1) Everything, as it is now.
            var rows = new Dictionary<string, List<Dictionary<string, object?>>>();
            foreach (var (table, _) in Tables)
                rows[table] = Read(connection, tx, $"SELECT * FROM \"{table}\"");

            // 2) A new UUID for every old id, in old-id order.
            var newIds = new Dictionary<string, Dictionary<long, Guid>>();
            foreach (var (table, _) in Tables)
            {
                newIds[table] = rows[table]
                    .Select(r => Convert.ToInt64(r["Id"]))
                    .OrderBy(id => id)
                    .ToDictionary(id => id, id => Guid.CreateVersion7(Origin.AddMilliseconds(id)));
            }

            // 3) Fresh tables from the current model - the old ones kept aside
            //    on Postgres (see the note at the top), dropped on SQLite.
            if (postgres)
            {
                Execute(connection, tx, $"CREATE SCHEMA \"{BackupSchema}\"");
                foreach (var table in Leftovers.Concat(Tables.Select(t => t.Table)))
                    Execute(connection, tx, $"ALTER TABLE IF EXISTS \"{table}\" SET SCHEMA \"{BackupSchema}\"");
                logger.LogInformation("UUID migration: the original tables are kept in schema {Schema}", BackupSchema);
            }
            else
            {
                foreach (var leftover in Leftovers.Concat(Tables.Select(t => t.Table).Reverse()))
                    Execute(connection, tx, $"DROP TABLE IF EXISTS \"{leftover}\"");
            }
            db.GetService<IRelationalDatabaseCreator>().CreateTables();

            // 4) The rows back in, ids swapped.
            var columnsByTable = db.Model.GetEntityTypes().ToDictionary(e => e.GetTableName()!, e => e.GetProperties().Select(p => p.GetColumnName()).ToHashSet());
            foreach (var (table, refs) in Tables)
            {
                var written = 0;
                foreach (var row in rows[table])
                {
                    var values = new Dictionary<string, object?>();
                    var orphan = false;
                    foreach (var (column, value) in row)
                    {
                        if (!columnsByTable[table].Contains(column)) continue; // a column the model no longer has

                        if (column == "Id")
                        {
                            values[column] = newIds[table][Convert.ToInt64(value)];
                        }
                        else if (refs.TryGetValue(column, out var target))
                        {
                            if (value == null) { values[column] = null; continue; }
                            if (target == HomeAwayTeam) target = row.GetValueOrDefault("IbbaGameCode") != null ? "IbbaTeams" : "Teams";
                            if (newIds[target].TryGetValue(Convert.ToInt64(value), out var mapped)) values[column] = mapped;
                            else if (column is "HomeTeamId" or "AwayTeamId" or "LiveTrackedByUserId" or "LinkedUserId" or "IbbaTeamId" or "GameId") values[column] = null; // pointed at a row already gone
                            else orphan = true;
                        }
                        else
                        {
                            values[column] = value;
                        }
                    }
                    if (orphan)
                    {
                        logger.LogWarning("UUID migration: skipped a {Table} row (id {Id}) that points at a row that no longer exists", table, row["Id"]);
                        continue;
                    }
                    Insert(connection, tx, table, values, postgres);
                    written++;
                }
                logger.LogInformation("UUID migration: {Table} {Written}/{Total} rows", table, written, rows[table].Count);
            }

            transaction.Commit();
            logger.LogInformation("UUID migration finished - every id is now a UUID");
        }

        // A table added to the model but missing here would be dropped and
        // recreated empty - refuse to run instead.
        private static void CheckEveryTableIsCovered(AppDbContext db)
        {
            var modelTables = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).ToHashSet();
            var covered = Tables.Select(t => t.Table).ToHashSet();
            if (!modelTables.SetEquals(covered))
                throw new InvalidOperationException($"UUID migration doesn't cover every table: model has {string.Join(", ", modelTables.Except(covered))}, list has {string.Join(", ", covered.Except(modelTables))}.");
        }

        private static void BackUpSqliteFile(AppDbContext db, ILogger logger)
        {
            var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
            var backup = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataSource))!, $"{Path.GetFileNameWithoutExtension(dataSource)}.before-uuid-{DateTime.UtcNow:yyyyMMddHHmmss}.db");
            db.Database.ExecuteSqlRaw($"VACUUM INTO '{backup.Replace("'", "''")}'");
            logger.LogInformation("UUID migration: backed up the database to {Backup}", backup);
        }

        private static List<Dictionary<string, object?>> Read(DbConnection connection, DbTransaction tx, string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            var result = new List<Dictionary<string, object?>>();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                result.Add(row);
            }
            return result;
        }

        private static void Insert(DbConnection connection, DbTransaction tx, string table, Dictionary<string, object?> values, bool postgres)
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            var columns = values.Keys.ToList();
            command.CommandText = $"INSERT INTO \"{table}\" ({string.Join(", ", columns.Select(c => $"\"{c}\""))}) VALUES ({string.Join(", ", columns.Select((_, i) => $"@p{i}"))})";
            for (var i = 0; i < columns.Count; i++)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = $"@p{i}";
                var value = values[columns[i]];
                // EF stores a Guid in SQLite as upper-case text.
                parameter.Value = value is Guid guid && !postgres ? guid.ToString().ToUpperInvariant() : value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
            command.ExecuteNonQuery();
        }

        private static void Execute(DbConnection connection, DbTransaction tx, string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static object? Scalar(AppDbContext db, string sql)
        {
            var connection = db.Database.GetDbConnection();
            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen) connection.Open();
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                return command.ExecuteScalar();
            }
            finally
            {
                if (!wasOpen) connection.Close();
            }
        }
    }
}
