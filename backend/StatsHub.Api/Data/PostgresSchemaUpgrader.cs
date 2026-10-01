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

            // Redesign: IbbaTeamLinks (per-player, duplicated team data) +
            // IbbaStandings (per-league rows) + IbbaTeamCrests (logo cache) are
            // replaced by one IbbaTeam table (one row per real team, shared by
            // everyone) plus a lean PlayerIbbaTeams join. No data migration - this
            // is IBBA-derived data, fully rebuilt by the next sync. Postgres (unlike
            // SQLite) allows dropping a column that carries a foreign key without
            // ceremony, but this is wrapped defensively anyway - the new
            // HomeTeamId/AwayTeamId columns below are all the new code actually
            // needs, so a failure here just leaves a harmless orphaned
            // column/tables rather than blocking startup.
            try
            {
                db.ExecuteSqlRaw(@"DROP TABLE IF EXISTS ""IbbaTeamLinks"";");
                db.ExecuteSqlRaw(@"DROP TABLE IF EXISTS ""IbbaStandings"";");
                db.ExecuteSqlRaw(@"DROP TABLE IF EXISTS ""IbbaTeamCrests"";");
                db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" DROP COLUMN IF EXISTS ""IbbaTeamLinkId"";");
                // The link moved from IbbaTeams.LinkedTeamId (one IbbaTeam -> at
                // most one Team - the actual bug, since two different players'
                // app Teams couldn't both link to the same real IBBA team) to
                // Teams.IbbaTeamId (many Teams -> one IbbaTeam). Postgres drops a
                // column's own FK/index along with it, so this is a clean drop
                // (unlike SQLite, which can't).
                db.ExecuteSqlRaw(@"ALTER TABLE ""IbbaTeams"" DROP COLUMN IF EXISTS ""LinkedTeamId"";");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not drop old IBBA tables/column (leaving them in place): {ex.Message}");
            }
            // Games.TeamId (a separate field for a manually-created game's
            // owner) and IsHomeGame (a separate bool alongside it) are both
            // gone - a manual game's own team id now lives in the very same
            // HomeTeamId/AwayTeamId pair an IBBA game uses (IbbaGameCode says
            // which table they mean: Teams for a manual game, IbbaTeams for a
            // synced one), with home/away encoded purely by which of the two
            // columns it's in. No formal FK on either column - a single column
            // can't reference two different tables - so every read resolves
            // them explicitly instead (GameService et al.). OpponentIbbaTeamId/
            // OwnIbbaTeamId (an earlier, still perspective-relative attempt)
            // are gone too. No data migration - this is dev-only data wiped
            // between iterations of this exact redesign.
            try
            {
                db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" DROP CONSTRAINT IF EXISTS ""FK_Games_Teams_TeamId"";");
                db.ExecuteSqlRaw(@"DROP INDEX IF EXISTS ""IX_Games_TeamId"";");
                db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" DROP COLUMN IF EXISTS ""TeamId"";");
                db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" DROP COLUMN IF EXISTS ""IsHomeGame"";");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not drop old Games.TeamId/IsHomeGame columns (leaving them in place): {ex.Message}");
            }
            db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" DROP COLUMN IF EXISTS ""OpponentIbbaTeamId"";");
            db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" DROP COLUMN IF EXISTS ""OwnIbbaTeamId"";");
            db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" ADD COLUMN IF NOT EXISTS ""HomeTeamId"" integer;");
            db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" ADD COLUMN IF NOT EXISTS ""AwayTeamId"" integer;");
            db.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Games_HomeTeamId"" ON ""Games"" (""HomeTeamId"");");
            db.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Games_AwayTeamId"" ON ""Games"" (""AwayTeamId"");");

            db.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ""IbbaTeams"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""IbbaTeamId"" TEXT NOT NULL,
                    ""TeamUrl"" TEXT NOT NULL,
                    ""Name"" TEXT NOT NULL,
                    ""LogoUrl"" TEXT,
                    ""LeagueUrl"" TEXT NOT NULL,
                    ""LeagueName"" TEXT NOT NULL,
                    ""LeaguePosition"" integer,
                    ""LeagueTotalTeams"" integer,
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
            db.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_IbbaTeams_TeamUrl"" ON ""IbbaTeams"" (""TeamUrl"");");
            db.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_IbbaTeams_LeagueUrl"" ON ""IbbaTeams"" (""LeagueUrl"");");

            db.ExecuteSqlRaw(@"ALTER TABLE ""Teams"" ADD COLUMN IF NOT EXISTS ""IbbaTeamId"" integer REFERENCES ""IbbaTeams"" (""Id"") ON DELETE SET NULL;");
            db.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Teams_IbbaTeamId"" ON ""Teams"" (""IbbaTeamId"");");

            db.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ""PlayerIbbaTeams"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""PlayerIbbaLinkId"" integer NOT NULL REFERENCES ""PlayerIbbaLinks"" (""Id"") ON DELETE CASCADE,
                    ""IbbaTeamId"" integer NOT NULL REFERENCES ""IbbaTeams"" (""Id"") ON DELETE CASCADE
                );");
            db.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PlayerIbbaTeams_PlayerIbbaLinkId_IbbaTeamId"" ON ""PlayerIbbaTeams"" (""PlayerIbbaLinkId"", ""IbbaTeamId"");");

            db.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Games_IbbaGameCode"" ON ""Games"" (""IbbaGameCode"");");

            // ---- Push notifications ----
            db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" ADD COLUMN IF NOT EXISTS ""ReminderSentAt"" timestamptz;");

            // Whoever starts a live game claims recording rights until it's no
            // longer "In Progress" - see Game.cs.
            db.ExecuteSqlRaw(@"ALTER TABLE ""Games"" ADD COLUMN IF NOT EXISTS ""LiveTrackedByUserId"" integer;");

            // Live on/off court indicator - see GameStats.OnCourt.
            db.ExecuteSqlRaw(@"ALTER TABLE ""GameStats"" ADD COLUMN IF NOT EXISTS ""OnCourt"" boolean;");

            // Ending every session of an account - see User.SecurityStamp.
            db.ExecuteSqlRaw(@"ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""SecurityStamp"" text;");

            db.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ""PushSubscriptions"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""UserId"" integer NOT NULL REFERENCES ""Users"" (""Id"") ON DELETE CASCADE,
                    ""Endpoint"" TEXT NOT NULL,
                    ""P256dh"" TEXT NOT NULL,
                    ""Auth"" TEXT NOT NULL,
                    ""CreatedAt"" timestamptz NOT NULL
                );");
            db.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PushSubscriptions_Endpoint"" ON ""PushSubscriptions"" (""Endpoint"");");
            db.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_PushSubscriptions_UserId"" ON ""PushSubscriptions"" (""UserId"");");

            // Guards against a race between two concurrent IBBA syncs inserting the
            // same fixture twice. Isolated in its own try/catch: if any duplicate
            // already snuck in before this shipped, creating the constraint would
            // fail - better to skip the safety net for now than crash every future
            // startup over it.
            try
            {
                db.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Games_IbbaGameCode_Unique"" ON ""Games"" (""IbbaGameCode"") WHERE ""IbbaGameCode"" IS NOT NULL;");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not create unique index on Games.IbbaGameCode (likely pre-existing duplicates): {ex.Message}");
            }
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
