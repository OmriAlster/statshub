using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StatsHub.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddScoresheetStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "JerseyFromScoresheet",
                table: "PlayerTeams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ScoresheetCode",
                table: "PlayerIbbaLinks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwishMatchId",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Turnovers",
                table: "GameStats",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "ThreePointersAttempted",
                table: "GameStats",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Steals",
                table: "GameStats",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "OffensiveRebounds",
                table: "GameStats",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "MinutesPlayed",
                table: "GameStats",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "FreeThrowsAttempted",
                table: "GameStats",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "FieldGoalsAttempted",
                table: "GameStats",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "DefensiveRebounds",
                table: "GameStats",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Blocks",
                table: "GameStats",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Assists",
                table: "GameStats",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "GameStats",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScoresheetLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Played = table.Column<bool>(type: "boolean", nullable: false),
                    Starter = table.Column<bool>(type: "boolean", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    FreeThrows = table.Column<int>(type: "integer", nullable: false),
                    TwoPointers = table.Column<int>(type: "integer", nullable: false),
                    ThreePointers = table.Column<int>(type: "integer", nullable: false),
                    Fouls = table.Column<int>(type: "integer", nullable: false),
                    SheetValid = table.Column<bool>(type: "boolean", nullable: false),
                    SheetErrors = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoresheetLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoresheetLines_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScoresheetLines_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScoresheetLines_GameId_PlayerId",
                table: "ScoresheetLines",
                columns: new[] { "GameId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScoresheetLines_PlayerId",
                table: "ScoresheetLines",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScoresheetLines");

            migrationBuilder.DropColumn(
                name: "JerseyFromScoresheet",
                table: "PlayerTeams");

            migrationBuilder.DropColumn(
                name: "ScoresheetCode",
                table: "PlayerIbbaLinks");

            migrationBuilder.DropColumn(
                name: "SwishMatchId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "GameStats");

            migrationBuilder.AlterColumn<int>(
                name: "Turnovers",
                table: "GameStats",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ThreePointersAttempted",
                table: "GameStats",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Steals",
                table: "GameStats",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "OffensiveRebounds",
                table: "GameStats",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "MinutesPlayed",
                table: "GameStats",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "FreeThrowsAttempted",
                table: "GameStats",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "FieldGoalsAttempted",
                table: "GameStats",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DefensiveRebounds",
                table: "GameStats",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Blocks",
                table: "GameStats",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Assists",
                table: "GameStats",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
