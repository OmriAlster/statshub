using Microsoft.EntityFrameworkCore;

namespace StatsHub.Api.Data
{
    // Postgres equivalent of SchemaUpgrader. EnsureCreated() is a complete no-op on
    // a database that already exists - it never adds tables or columns introduced
    // by model changes made after the database's first creation, regardless of
    // provider. SchemaUpgrader.cs only ever ran for SQLite, so any schema change
    // shipped after the first production deploy (e.g. PlayerTeams.JerseyNumber,
    // the PlayerParents table) may never have actually reached Postgres. Unlike
    // SQLite, Postgres supports "IF NOT EXISTS" directly on ALTER/CREATE, so this
    // doesn't need SchemaUpgrader's manual existence-check dance - every statement
    // here is naturally idempotent and safe to re-run on every startup.
    public static class PostgresSchemaUpgrader
    {
        public static void Apply(AppDbContext context)
        {
            var db = context.Database;

            // ---- Historical additions, backfilled so a production database
            // created before any of these features shipped still ends up with
            // today's full schema. ----
            db.ExecuteSqlRaw(@"ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""PasswordHash"" TEXT;");
            db.ExecuteSqlRaw(@"ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""InvitePasswordHash"" TEXT;");
            db.ExecuteSqlRaw(@"ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""ParentInviteCode"" TEXT;");
            db.ExecuteSqlRaw(@"ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""ParentInviteCodeExpiresAt"" timestamptz;");
            db.ExecuteSqlRaw(@"ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""ParentInvitePasswordHash"" TEXT;");
            db.ExecuteSqlRaw(@"ALTER TABLE ""PlayerTeams"" ADD COLUMN IF NOT EXISTS ""JerseyNumber"" integer NOT NULL DEFAULT 0;");

            db.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ""PlayerParents"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""PlayerId"" integer NOT NULL REFERENCES ""Players"" (""Id"") ON DELETE CASCADE,
                    ""UserId"" integer NOT NULL REFERENCES ""Users"" (""Id"") ON DELETE CASCADE,
                    ""CreatedAt"" timestamptz NOT NULL
                );");
            db.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PlayerParents_PlayerId_UserId"" ON ""PlayerParents"" (""PlayerId"", ""UserId"");");
            db.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_PlayerParents_UserId"" ON ""PlayerParents"" (""UserId"");");
            db.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Players_ParentInviteCode"" ON ""Players"" (""ParentInviteCode"");");

            // Backfill: every existing player's creating user becomes a PlayerParent
            // row, so nothing changes for households that only have one parent.
            db.ExecuteSqlRaw(@"
                INSERT INTO ""PlayerParents"" (""PlayerId"", ""UserId"", ""CreatedAt"")
                SELECT p.""Id"", p.""UserId"", now()
                FROM ""Players"" p
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""PlayerParents"" pp
                    WHERE pp.""PlayerId"" = p.""Id"" AND pp.""UserId"" = p.""UserId""
                );");

            // Backfill: give every existing PlayerTeam row the player's old
            // (pre-migration) jersey number so per-team numbers start populated,
            // before that column is dropped below. No-op once already applied.
            if (ColumnExists(context, "Players", "JerseyNumber"))
            {
                db.ExecuteSqlRaw(@"
                    UPDATE ""PlayerTeams""
                    SET ""JerseyNumber"" = (SELECT p.""JerseyNumber"" FROM ""Players"" p WHERE p.""Id"" = ""PlayerTeams"".""PlayerId"")
                    WHERE ""JerseyNumber"" = 0;");
            }

            // Jersey number is per-team now (PlayerTeams.JerseyNumber) - the old
            // player-level default is gone.
            db.ExecuteSqlRaw(@"ALTER TABLE ""Players"" DROP COLUMN IF EXISTS ""JerseyNumber"";");

            // ---- IBBA integration ----
            db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" ADD COLUMN IF NOT EXISTS ""IbbaGameCode"" TEXT;");
            db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" ADD COLUMN IF NOT EXISTS ""IbbaTeamLinkId"" integer;");
            db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" ADD COLUMN IF NOT EXISTS ""IsHomeGame"" boolean;");

            db.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ""PlayerIbbaLinks"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""PlayerId"" integer NOT NULL REFERENCES ""Players"" (""Id"") ON DELETE CASCADE,
                    ""IbbaPlayerUrl"" TEXT NOT NULL,
                    ""LastSyncedAt"" timestamptz,
                    ""LastSyncError"" TEXT,
                    ""CreatedAt"" timestamptz NOT NULL
                );");
            db.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PlayerIbbaLinks_PlayerId"" ON ""PlayerIbbaLinks"" (""PlayerId"");");

            db.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ""IbbaTeamLinks"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""PlayerIbbaLinkId"" integer NOT NULL REFERENCES ""PlayerIbbaLinks"" (""Id"") ON DELETE CASCADE,
                    ""IbbaTeamSlugId"" TEXT NOT NULL,
                    ""IbbaTeamExportId"" TEXT NOT NULL,
                    ""TeamName"" TEXT NOT NULL,
                    ""TeamUrl"" TEXT NOT NULL,
                    ""TeamLogoUrl"" TEXT,
                    ""LinkedTeamId"" integer REFERENCES ""Teams"" (""Id"") ON DELETE SET NULL,
                    ""IbbaLeagueUrl"" TEXT,
                    ""IbbaLeagueName"" TEXT
                );");
            db.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_IbbaTeamLinks_PlayerIbbaLinkId"" ON ""IbbaTeamLinks"" (""PlayerIbbaLinkId"");");

            db.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ""IbbaStandings"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""IbbaLeagueUrl"" TEXT NOT NULL,
                    ""IbbaLeagueName"" TEXT NOT NULL,
                    ""Position"" integer NOT NULL,
                    ""TeamName"" TEXT NOT NULL,
                    ""TeamUrl"" TEXT NOT NULL,
                    ""GamesPlayed"" integer NOT NULL,
                    ""Wins"" integer NOT NULL,
                    ""Losses"" integer NOT NULL,
                    ""Technical"" integer NOT NULL,
                    ""PointsFor"" integer NOT NULL,
                    ""PointsAgainst"" integer NOT NULL,
                    ""Diff"" integer NOT NULL,
                    ""LeaguePoints"" integer NOT NULL,
                    ""SyncedAt"" timestamptz NOT NULL
                );");
            db.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_IbbaStandings_IbbaLeagueUrl"" ON ""IbbaStandings"" (""IbbaLeagueUrl"");");
            db.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Games_IbbaGameCode"" ON ""Games"" (""IbbaGameCode"");");
        }

        private static bool ColumnExists(AppDbContext context, string table, string column)
        {
            var connection = context.Database.GetDbConnection();
            var wasClosed = connection.State != System.Data.ConnectionState.Open;
            if (wasClosed) connection.Open();
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM information_schema.columns WHERE table_name = @table AND column_name = @column;";
                var tableParam = cmd.CreateParameter();
                tableParam.ParameterName = "@table";
                tableParam.Value = table;
                cmd.Parameters.Add(tableParam);
                var columnParam = cmd.CreateParameter();
                columnParam.ParameterName = "@column";
                columnParam.Value = column;
                cmd.Parameters.Add(columnParam);
                using var reader = cmd.ExecuteReader();
                return reader.Read();
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }
    }
}
