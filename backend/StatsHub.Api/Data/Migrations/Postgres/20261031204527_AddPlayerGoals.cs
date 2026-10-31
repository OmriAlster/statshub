using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StatsHub.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddPlayerGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stat = table.Column<string>(type: "text", nullable: false),
                    Target = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerGoals_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGoals_PlayerId_Stat",
                table: "PlayerGoals",
                columns: new[] { "PlayerId", "Stat" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerGoals");
        }
    }
}
