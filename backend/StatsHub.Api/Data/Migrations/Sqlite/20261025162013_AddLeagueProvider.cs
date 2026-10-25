using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StatsHub.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddLeagueProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Games_IbbaGameCode",
                table: "Games");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "PlayerIbbaLinks",
                type: "TEXT",
                nullable: false,
                defaultValue: "ibba");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "IbbaTeams",
                type: "TEXT",
                nullable: false,
                defaultValue: "ibba");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Games",
                type: "TEXT",
                nullable: true);

            // Every synced game so far is an IBBA game - before the new unique
            // index, so it's unique per league from the start.
            migrationBuilder.Sql("UPDATE \"Games\" SET \"Provider\" = 'ibba' WHERE \"IbbaGameCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IbbaTeams_Provider_IbbaTeamId",
                table: "IbbaTeams",
                columns: new[] { "Provider", "IbbaTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Games_Provider_IbbaGameCode",
                table: "Games",
                columns: new[] { "Provider", "IbbaGameCode" },
                unique: true,
                filter: "\"IbbaGameCode\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IbbaTeams_Provider_IbbaTeamId",
                table: "IbbaTeams");

            migrationBuilder.DropIndex(
                name: "IX_Games_Provider_IbbaGameCode",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "PlayerIbbaLinks");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "IbbaTeams");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Games");

            migrationBuilder.CreateIndex(
                name: "IX_Games_IbbaGameCode",
                table: "Games",
                column: "IbbaGameCode",
                unique: true,
                filter: "\"IbbaGameCode\" IS NOT NULL");
        }
    }
}
