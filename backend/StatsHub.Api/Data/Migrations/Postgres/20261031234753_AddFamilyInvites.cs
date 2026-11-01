using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StatsHub.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddFamilyInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FamilyInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyInvites_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FamilyInviteJoins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InviteId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyInviteJoins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyInviteJoins_FamilyInvites_InviteId",
                        column: x => x.InviteId,
                        principalTable: "FamilyInvites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FamilyInviteJoins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FamilyInvitePlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InviteId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyInvitePlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyInvitePlayers_FamilyInvites_InviteId",
                        column: x => x.InviteId,
                        principalTable: "FamilyInvites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FamilyInvitePlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyInviteJoins_InviteId",
                table: "FamilyInviteJoins",
                column: "InviteId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyInviteJoins_UserId",
                table: "FamilyInviteJoins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyInvitePlayers_InviteId",
                table: "FamilyInvitePlayers",
                column: "InviteId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyInvitePlayers_PlayerId",
                table: "FamilyInvitePlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyInvites_Code",
                table: "FamilyInvites",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyInvites_CreatedByUserId",
                table: "FamilyInvites",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FamilyInviteJoins");

            migrationBuilder.DropTable(
                name: "FamilyInvitePlayers");

            migrationBuilder.DropTable(
                name: "FamilyInvites");
        }
    }
}
