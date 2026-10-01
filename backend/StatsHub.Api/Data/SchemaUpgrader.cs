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

                // Backfill: give every existing PlayerTeam row the player's old
                // (pre-migration) jersey number so per-team numbers start populated,
                // before that column is dropped below. No-op once already applied.
                if (ColumnExists(connection, "Players", "JerseyNumber"))
                {
                    ExecuteNonQuery(connection, @"
                        UPDATE ""PlayerTeams""
                        SET ""JerseyNumber"" = (SELECT p.""JerseyNumber"" FROM ""Players"" p WHERE p.""Id"" = ""PlayerTeams"".""PlayerId"")
                        WHERE ""JerseyNumber"" = 0;");
                }

                // Jersey number is per-team now (PlayerTeams.JerseyNumber) - the old
                // player-level default is gone.
                DropColumnIfExists(connection, "Players", "JerseyNumber");

                // IBBA integration
                AddColumnIfMissing(connection, "Games", "IbbaGameCode", "TEXT");
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

                // Redesign: IbbaTeamLinks (per-player, duplicated team data) +
                // IbbaStandings (per-league rows) + IbbaTeamCrests (logo cache) are
                // replaced by one IbbaTeam table (one row per real team, shared by
                // everyone) plus a lean PlayerIbbaTeams join. No data migration -
                // this is IBBA-derived data, fully rebuilt by the next sync.
                // IbbaStandings/IbbaTeamCrests have no incoming foreign keys from
                // any surviving table, so dropping them outright is safe.
                DropTableIfExists(connection, "IbbaStandings");
                DropTableIfExists(connection, "IbbaTeamCrests");

                // IbbaTeamLinks is different: Games.IbbaTeamLinkId still has a
                // foreign key pointing at it baked into Games' own on-disk schema
                // (SQLite refuses to DROP COLUMN a column that's part of an FK
                // definition, even once the table it references is gone - that's
                // not a "the table doesn't exist yet" check, it's unconditional).
                // Dropping the referenced table anyway leaves that FK orphaned,
                // and SQLite's FK enforcement then fails on *every* future write
                // to Games with "no such table: IbbaTeamLinks" - not a hypothetical,
                // this actually happened. So the table has to stay - recreated here
                // if an earlier version of this upgrader already dropped it - as
                // inert, empty, never-written-to-by-new-code dead weight, rather
                // than attempting SQLite's full rebuild-and-rename dance to
                // properly remove a column nothing needs gone.
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

                // Games.TeamId (a separate field for a manually-created game's
                // owner) and IsHomeGame (a separate bool alongside it) are both
                // gone - a manual game's own team id now lives in the very same
                // HomeTeamId/AwayTeamId pair an IBBA game uses (IbbaGameCode says
                // which table they mean: Teams for a manual game, IbbaTeams for a
                // synced one), with home/away encoded purely by which of the two
                // columns it's in. OpponentIbbaTeamId/OwnIbbaTeamId (an earlier,
                // still perspective-relative attempt) are gone too. All were
                // added via plain ALTER ADD COLUMN (no inline FK, unlike a CREATE
                // TABLE-defined one), so - unlike IbbaTeamLinks above - SQLite
                // has no objection to dropping them outright; wrapped
                // defensively anyway. No data migration - this is dev-only data
                // wiped between iterations of this exact redesign.
                try
                {
                    ExecuteNonQuery(connection, "DROP INDEX IF EXISTS \"IX_Games_OwnIbbaTeamId\";");
                    ExecuteNonQuery(connection, "DROP INDEX IF EXISTS \"IX_Games_TeamId\";");
                    DropColumnIfExists(connection, "Games", "OpponentIbbaTeamId");
                    DropColumnIfExists(connection, "Games", "OwnIbbaTeamId");
                    DropColumnIfExists(connection, "Games", "TeamId");
                    DropColumnIfExists(connection, "Games", "IsHomeGame");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Could not drop old Games team/home columns (leaving them in place): {ex.Message}");
                }

                AddColumnIfMissing(connection, "Games", "HomeTeamId", "INTEGER");
                AddColumnIfMissing(connection, "Games", "AwayTeamId", "INTEGER");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS \"IX_Games_HomeTeamId\" ON \"Games\" (\"HomeTeamId\");");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS \"IX_Games_AwayTeamId\" ON \"Games\" (\"AwayTeamId\");");

                CreateTableIfMissing(connection, @"
                    CREATE TABLE IF NOT EXISTS ""IbbaTeams"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_IbbaTeams"" PRIMARY KEY AUTOINCREMENT,
                        ""IbbaTeamId"" TEXT NOT NULL,
                        ""TeamUrl"" TEXT NOT NULL,
                        ""Name"" TEXT NOT NULL,
                        ""LogoUrl"" TEXT NULL,
                        ""LeagueUrl"" TEXT NOT NULL,
                        ""LeagueName"" TEXT NOT NULL,
                        ""LeaguePosition"" INTEGER NULL,
                        ""LeagueTotalTeams"" INTEGER NULL,
                        ""GamesPlayed"" INTEGER NOT NULL,
                        ""Wins"" INTEGER NOT NULL,
                        ""Losses"" INTEGER NOT NULL,
                        ""Technical"" INTEGER NOT NULL,
                        ""PointsFor"" INTEGER NOT NULL,
                        ""PointsAgainst"" INTEGER NOT NULL,
                        ""Diff"" INTEGER NOT NULL,
                        ""LeaguePoints"" INTEGER NOT NULL,
                        ""SyncedAt"" TEXT NOT NULL,
                        ""LinkedTeamId"" INTEGER NULL,
                        CONSTRAINT ""FK_IbbaTeams_Teams_LinkedTeamId"" FOREIGN KEY (""LinkedTeamId"") REFERENCES ""Teams"" (""Id"") ON DELETE SET NULL
                    );");
                ExecuteNonQuery(connection, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_IbbaTeams_TeamUrl\" ON \"IbbaTeams\" (\"TeamUrl\");");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS \"IX_IbbaTeams_LeagueUrl\" ON \"IbbaTeams\" (\"LeagueUrl\");");

                // The link moved from IbbaTeams.LinkedTeamId (one IbbaTeam -> at
                // most one Team, which was the actual bug: two different players'
                // app Teams couldn't both link to the same real IBBA team) to
                // Teams.IbbaTeamId (many Teams -> one IbbaTeam). The old column
                // stays - same unavoidable SQLite restriction as IbbaTeamLinks
                // above, a column can't be dropped while it's part of an FK
                // definition - but the unique index on it is gone since nothing
                // reads/writes that column anymore, and dropping an index has no
                // such restriction.
                ExecuteNonQuery(connection, "DROP INDEX IF EXISTS \"IX_IbbaTeams_LinkedTeamId\";");

                AddColumnIfMissing(connection, "Teams", "IbbaTeamId", "INTEGER");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS \"IX_Teams_IbbaTeamId\" ON \"Teams\" (\"IbbaTeamId\");");

                CreateTableIfMissing(connection, @"
                    CREATE TABLE IF NOT EXISTS ""PlayerIbbaTeams"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PlayerIbbaTeams"" PRIMARY KEY AUTOINCREMENT,
                        ""PlayerIbbaLinkId"" INTEGER NOT NULL,
                        ""IbbaTeamId"" INTEGER NOT NULL,
                        CONSTRAINT ""FK_PlayerIbbaTeams_PlayerIbbaLinks_PlayerIbbaLinkId"" FOREIGN KEY (""PlayerIbbaLinkId"") REFERENCES ""PlayerIbbaLinks"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_PlayerIbbaTeams_IbbaTeams_IbbaTeamId"" FOREIGN KEY (""IbbaTeamId"") REFERENCES ""IbbaTeams"" (""Id"") ON DELETE CASCADE
                    );");
                ExecuteNonQuery(connection, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_PlayerIbbaTeams_PlayerIbbaLinkId_IbbaTeamId\" ON \"PlayerIbbaTeams\" (\"PlayerIbbaLinkId\", \"IbbaTeamId\");");

                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS \"IX_Games_IbbaGameCode\" ON \"Games\" (\"IbbaGameCode\");");

                // Push notifications
                AddColumnIfMissing(connection, "Games", "ReminderSentAt", "TEXT");

                // Whoever starts a live game claims recording rights until it's
                // no longer "In Progress" - see Game.cs.
                AddColumnIfMissing(connection, "Games", "LiveTrackedByUserId", "INTEGER");

                // Live on/off court indicator - see GameStats.OnCourt.
                AddColumnIfMissing(connection, "GameStats", "OnCourt", "INTEGER");

                CreateTableIfMissing(connection, @"
                    CREATE TABLE IF NOT EXISTS ""PushSubscriptions"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PushSubscriptions"" PRIMARY KEY AUTOINCREMENT,
                        ""UserId"" INTEGER NOT NULL,
                        ""Endpoint"" TEXT NOT NULL,
                        ""P256dh"" TEXT NOT NULL,
                        ""Auth"" TEXT NOT NULL,
                        ""CreatedAt"" TEXT NOT NULL,
                        CONSTRAINT ""FK_PushSubscriptions_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE
                    );");
                ExecuteNonQuery(connection, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_PushSubscriptions_Endpoint\" ON \"PushSubscriptions\" (\"Endpoint\");");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS \"IX_PushSubscriptions_UserId\" ON \"PushSubscriptions\" (\"UserId\");");

                // Guards against a race between two concurrent IBBA syncs inserting
                // the same fixture twice. Isolated in its own try/catch: if any
                // duplicate already snuck in before this shipped, creating the
                // constraint would fail - better to skip the safety net for now than
                // crash every future startup over it.
                try
                {
                    ExecuteNonQuery(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Games_IbbaGameCode_Unique\" ON \"Games\" (\"IbbaGameCode\") WHERE \"IbbaGameCode\" IS NOT NULL;");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Could not create unique index on Games.IbbaGameCode (likely pre-existing duplicates): {ex.Message}");
                }
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        private static void AddColumnIfMissing(System.Data.Common.DbConnection connection, string table, string column, string columnDefSql)
        {
            if (!ColumnExists(connection, table, column))
            {
                ExecuteNonQuery(connection, $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {columnDefSql};");
            }
        }

        private static void DropColumnIfExists(System.Data.Common.DbConnection connection, string table, string column)
        {
            if (ColumnExists(connection, table, column))
            {
                ExecuteNonQuery(connection, $"ALTER TABLE \"{table}\" DROP COLUMN \"{column}\";");
            }
        }

        private static bool ColumnExists(System.Data.Common.DbConnection connection, string table, string column)
        {
            using var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = $"PRAGMA table_info(\"{table}\");";
            using var reader = checkCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void CreateTableIfMissing(System.Data.Common.DbConnection connection, string createSql) =>
            ExecuteNonQuery(connection, createSql);

        private static void DropTableIfExists(System.Data.Common.DbConnection connection, string table) =>
            ExecuteNonQuery(connection, $"DROP TABLE IF EXISTS \"{table}\";");

        private static void ExecuteNonQuery(System.Data.Common.DbConnection connection, string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
