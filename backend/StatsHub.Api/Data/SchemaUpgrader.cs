using Microsoft.EntityFrameworkCore;

namespace StatsHub.Api.Data
{
    // The app uses Database.EnsureCreated() instead of EF migrations, which only
    // creates the schema for a brand-new database file - it never alters an
    // existing one. This runs small, idempotent ALTER/CREATE statements against
    // an already-existing statshub.db so new columns/tables show up without
    // losing any previously recorded games or players.
    public static class SchemaUpgrader
    {
        public static void Apply(AppDbContext context)
        {
            var connection = context.Database.GetDbConnection();
            var wasClosed = connection.State != System.Data.ConnectionState.Open;
            if (wasClosed) connection.Open();

            try
            {
                AddColumnIfMissing(connection, "Users", "PasswordHash", "TEXT");
                AddColumnIfMissing(connection, "Players", "InvitePasswordHash", "TEXT");
                AddColumnIfMissing(connection, "Players", "ParentInviteCode", "TEXT");
                AddColumnIfMissing(connection, "Players", "ParentInviteCodeExpiresAt", "TEXT");
                AddColumnIfMissing(connection, "Players", "ParentInvitePasswordHash", "TEXT");
                AddColumnIfMissing(connection, "PlayerTeams", "JerseyNumber", "INTEGER NOT NULL DEFAULT 0");

                CreateTableIfMissing(connection, @"
                    CREATE TABLE IF NOT EXISTS ""PlayerParents"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PlayerParents"" PRIMARY KEY AUTOINCREMENT,
                        ""PlayerId"" INTEGER NOT NULL,
                        ""UserId"" INTEGER NOT NULL,
                        ""CreatedAt"" TEXT NOT NULL,
                        CONSTRAINT ""FK_PlayerParents_Players_PlayerId"" FOREIGN KEY (""PlayerId"") REFERENCES ""Players"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_PlayerParents_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE
                    );");
                ExecuteNonQuery(connection, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_PlayerParents_PlayerId_UserId\" ON \"PlayerParents\" (\"PlayerId\", \"UserId\");");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS \"IX_PlayerParents_UserId\" ON \"PlayerParents\" (\"UserId\");");
                ExecuteNonQuery(connection, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Players_ParentInviteCode\" ON \"Players\" (\"ParentInviteCode\");");

                // Backfill: every existing player's creating user becomes a PlayerParent
                // row, so nothing changes for households that only have one parent.
                ExecuteNonQuery(connection, @"
                    INSERT INTO ""PlayerParents"" (""PlayerId"", ""UserId"", ""CreatedAt"")
                    SELECT p.""Id"", p.""UserId"", CURRENT_TIMESTAMP
                    FROM ""Players"" p
                    WHERE NOT EXISTS (
                        SELECT 1 FROM ""PlayerParents"" pp
                        WHERE pp.""PlayerId"" = p.""Id"" AND pp.""UserId"" = p.""UserId""
                    );");

                // Backfill: give every existing PlayerTeam row the player's current
                // (pre-migration) jersey number so per-team numbers start populated.
                ExecuteNonQuery(connection, @"
                    UPDATE ""PlayerTeams""
                    SET ""JerseyNumber"" = (SELECT p.""JerseyNumber"" FROM ""Players"" p WHERE p.""Id"" = ""PlayerTeams"".""PlayerId"")
                    WHERE ""JerseyNumber"" = 0;");
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        private static void AddColumnIfMissing(System.Data.Common.DbConnection connection, string table, string column, string columnDefSql)
        {
            using var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = $"PRAGMA table_info(\"{table}\");";
            using var reader = checkCmd.ExecuteReader();
            var exists = false;
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
            reader.Close();

            if (!exists)
            {
                ExecuteNonQuery(connection, $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {columnDefSql};");
            }
        }

        private static void CreateTableIfMissing(System.Data.Common.DbConnection connection, string createSql) =>
            ExecuteNonQuery(connection, createSql);

        private static void ExecuteNonQuery(System.Data.Common.DbConnection connection, string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
