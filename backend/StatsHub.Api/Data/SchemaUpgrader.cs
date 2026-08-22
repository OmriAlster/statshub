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

                // IBBA integration
                AddColumnIfMissing(connection, "Games", "IbbaGameCode", "TEXT");
                AddColumnIfMissing(connection, "Games", "IbbaTeamLinkId", "INTEGER");
                AddColumnIfMissing(connection, "Games", "IsHomeGame", "INTEGER");

                CreateTableIfMissing(connection, @"
                    CREATE TABLE IF NOT EXISTS ""PlayerIbbaLinks"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PlayerIbbaLinks"" PRIMARY KEY AUTOINCREMENT,
                        ""PlayerId"" INTEGER NOT NULL,
                        ""IbbaPlayerUrl"" TEXT NOT NULL,
                        ""LastSyncedAt"" TEXT NULL,
                        ""LastSyncError"" TEXT NULL,
                        ""CreatedAt"" TEXT NOT NULL,
                        CONSTRAINT ""FK_PlayerIbbaLinks_Players_PlayerId"" FOREIGN KEY (""PlayerId"") REFERENCES ""Players"" (""Id"") ON DELETE CASCADE
                    );");
                ExecuteNonQuery(connection, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_PlayerIbbaLinks_PlayerId\" ON \"PlayerIbbaLinks\" (\"PlayerId\");");

                CreateTableIfMissing(connection, @"
                    CREATE TABLE IF NOT EXISTS ""IbbaTeamLinks"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_IbbaTeamLinks"" PRIMARY KEY AUTOINCREMENT,
                        ""PlayerIbbaLinkId"" INTEGER NOT NULL,
                        ""IbbaTeamSlugId"" TEXT NOT NULL,
                        ""IbbaTeamExportId"" TEXT NOT NULL,
                        ""TeamName"" TEXT NOT NULL,
                        ""TeamUrl"" TEXT NOT NULL,
                        ""TeamLogoUrl"" TEXT NULL,
                        ""LinkedTeamId"" INTEGER NULL,
                        ""IbbaLeagueUrl"" TEXT NULL,
                        ""IbbaLeagueName"" TEXT NULL,
                        CONSTRAINT ""FK_IbbaTeamLinks_PlayerIbbaLinks_PlayerIbbaLinkId"" FOREIGN KEY (""PlayerIbbaLinkId"") REFERENCES ""PlayerIbbaLinks"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_IbbaTeamLinks_Teams_LinkedTeamId"" FOREIGN KEY (""LinkedTeamId"") REFERENCES ""Teams"" (""Id"") ON DELETE SET NULL
                    );");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS \"IX_IbbaTeamLinks_PlayerIbbaLinkId\" ON \"IbbaTeamLinks\" (\"PlayerIbbaLinkId\");");

                CreateTableIfMissing(connection, @"
                    CREATE TABLE IF NOT EXISTS ""IbbaStandings"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_IbbaStandings"" PRIMARY KEY AUTOINCREMENT,
                        ""IbbaLeagueUrl"" TEXT NOT NULL,
                        ""IbbaLeagueName"" TEXT NOT NULL,
                        ""Position"" INTEGER NOT NULL,
                        ""TeamName"" TEXT NOT NULL,
                        ""TeamUrl"" TEXT NOT NULL,
                        ""GamesPlayed"" INTEGER NOT NULL,
                        ""Wins"" INTEGER NOT NULL,
                        ""Losses"" INTEGER NOT NULL,
                        ""Technical"" INTEGER NOT NULL,
                        ""PointsFor"" INTEGER NOT NULL,
                        ""PointsAgainst"" INTEGER NOT NULL,
                        ""Diff"" INTEGER NOT NULL,
                        ""LeaguePoints"" INTEGER NOT NULL,
                        ""SyncedAt"" TEXT NOT NULL
                    );");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS \"IX_IbbaStandings_IbbaLeagueUrl\" ON \"IbbaStandings\" (\"IbbaLeagueUrl\");");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS \"IX_Games_IbbaGameCode\" ON \"Games\" (\"IbbaGameCode\");");
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
