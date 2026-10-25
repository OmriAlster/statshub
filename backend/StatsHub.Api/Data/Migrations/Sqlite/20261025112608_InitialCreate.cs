using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StatsHub.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameType = table.Column<string>(type: "TEXT", nullable: false),
                    GameDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReminderSentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LiveTrackedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IbbaGameCode = table.Column<string>(type: "TEXT", nullable: true),
                    HomeTeamId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AwayTeamId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OpponentName = table.Column<string>(type: "TEXT", nullable: false),
                    TeamScore = table.Column<int>(type: "INTEGER", nullable: true),
                    OpponentScore = table.Column<int>(type: "INTEGER", nullable: true),
                    HomeScore = table.Column<int>(type: "INTEGER", nullable: true),
                    AwayScore = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IbbaTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IbbaTeamId = table.Column<string>(type: "TEXT", nullable: false),
                    TeamUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LogoUrl = table.Column<string>(type: "TEXT", nullable: true),
                    LeagueUrl = table.Column<string>(type: "TEXT", nullable: false),
                    LeagueName = table.Column<string>(type: "TEXT", nullable: false),
                    LeaguePosition = table.Column<int>(type: "INTEGER", nullable: true),
                    LeagueTotalTeams = table.Column<int>(type: "INTEGER", nullable: true),
                    GamesPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false),
                    Technical = table.Column<int>(type: "INTEGER", nullable: false),
                    PointsFor = table.Column<int>(type: "INTEGER", nullable: false),
                    PointsAgainst = table.Column<int>(type: "INTEGER", nullable: false),
                    Diff = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaguePoints = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IbbaTeams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", nullable: false),
                    LastName = table.Column<string>(type: "TEXT", nullable: false),
                    GoogleId = table.Column<string>(type: "TEXT", nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ProfilePictureUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", nullable: false),
                    LastName = table.Column<string>(type: "TEXT", nullable: false),
                    Position = table.Column<string>(type: "TEXT", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    Weight = table.Column<int>(type: "INTEGER", nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProfilePictureUrl = table.Column<string>(type: "TEXT", nullable: true),
                    LinkedUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InviteCode = table.Column<string>(type: "TEXT", nullable: true),
                    InviteCodeExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    InvitePasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    ParentInviteCode = table.Column<string>(type: "TEXT", nullable: true),
                    ParentInviteCodeExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ParentInvitePasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_Users_LinkedUserId",
                        column: x => x.LinkedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Players_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: false),
                    P256dh = table.Column<string>(type: "TEXT", nullable: false),
                    Auth = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Sport = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FieldGoalsMade = table.Column<int>(type: "INTEGER", nullable: false),
                    FieldGoalsAttempted = table.Column<int>(type: "INTEGER", nullable: false),
                    ThreePointersMade = table.Column<int>(type: "INTEGER", nullable: false),
                    ThreePointersAttempted = table.Column<int>(type: "INTEGER", nullable: false),
                    FreeThrowsMade = table.Column<int>(type: "INTEGER", nullable: false),
                    FreeThrowsAttempted = table.Column<int>(type: "INTEGER", nullable: false),
                    OffensiveRebounds = table.Column<int>(type: "INTEGER", nullable: false),
                    DefensiveRebounds = table.Column<int>(type: "INTEGER", nullable: false),
                    Assists = table.Column<int>(type: "INTEGER", nullable: false),
                    Steals = table.Column<int>(type: "INTEGER", nullable: false),
                    Blocks = table.Column<int>(type: "INTEGER", nullable: false),
                    Turnovers = table.Column<int>(type: "INTEGER", nullable: false),
                    Fouls = table.Column<int>(type: "INTEGER", nullable: false),
                    MinutesPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    OnCourt = table.Column<bool>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameStats_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameStats_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerIbbaLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IbbaPlayerUrl = table.Column<string>(type: "TEXT", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerIbbaLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerIbbaLinks_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerParents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerParents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerParents_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerParents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShareLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Token = table.Column<string>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShareLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShareLinks_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShareLinks_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IbbaTeamId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_IbbaTeams_IbbaTeamId",
                        column: x => x.IbbaTeamId,
                        principalTable: "IbbaTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Teams_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Shots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameStatsId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quarter = table.Column<int>(type: "INTEGER", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    Made = table.Column<bool>(type: "INTEGER", nullable: false),
                    Value = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shots_GameStats_GameStatsId",
                        column: x => x.GameStatsId,
                        principalTable: "GameStats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerIbbaTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerIbbaLinkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IbbaTeamId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerIbbaTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerIbbaTeams_IbbaTeams_IbbaTeamId",
                        column: x => x.IbbaTeamId,
                        principalTable: "IbbaTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerIbbaTeams_PlayerIbbaLinks_PlayerIbbaLinkId",
                        column: x => x.PlayerIbbaLinkId,
                        principalTable: "PlayerIbbaLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JerseyNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerTeams_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerTeams_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameStats_GameId_PlayerId",
                table: "GameStats",
                columns: new[] { "GameId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameStats_PlayerId",
                table: "GameStats",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_AwayTeamId",
                table: "Games",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_HomeTeamId",
                table: "Games",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_IbbaGameCode",
                table: "Games",
                column: "IbbaGameCode",
                unique: true,
                filter: "\"IbbaGameCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IbbaTeams_LeagueUrl",
                table: "IbbaTeams",
                column: "LeagueUrl");

            migrationBuilder.CreateIndex(
                name: "IX_IbbaTeams_TeamUrl",
                table: "IbbaTeams",
                column: "TeamUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerIbbaLinks_PlayerId",
                table: "PlayerIbbaLinks",
                column: "PlayerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerIbbaTeams_IbbaTeamId",
                table: "PlayerIbbaTeams",
                column: "IbbaTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerIbbaTeams_PlayerIbbaLinkId_IbbaTeamId",
                table: "PlayerIbbaTeams",
                columns: new[] { "PlayerIbbaLinkId", "IbbaTeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerParents_PlayerId_UserId",
                table: "PlayerParents",
                columns: new[] { "PlayerId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerParents_UserId",
                table: "PlayerParents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTeams_PlayerId_TeamId",
                table: "PlayerTeams",
                columns: new[] { "PlayerId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTeams_TeamId",
                table: "PlayerTeams",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_InviteCode",
                table: "Players",
                column: "InviteCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_LinkedUserId",
                table: "Players",
                column: "LinkedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_ParentInviteCode",
                table: "Players",
                column: "ParentInviteCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_UserId",
                table: "Players",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_Endpoint",
                table: "PushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId",
                table: "PushSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_UserId",
                table: "Seasons",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShareLinks_GameId",
                table: "ShareLinks",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_ShareLinks_PlayerId",
                table: "ShareLinks",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ShareLinks_Token",
                table: "ShareLinks",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shots_GameStatsId",
                table: "Shots",
                column: "GameStatsId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_IbbaTeamId",
                table: "Teams",
                column: "IbbaTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_SeasonId",
                table: "Teams",
                column: "SeasonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerIbbaTeams");

            migrationBuilder.DropTable(
                name: "PlayerParents");

            migrationBuilder.DropTable(
                name: "PlayerTeams");

            migrationBuilder.DropTable(
                name: "PushSubscriptions");

            migrationBuilder.DropTable(
                name: "ShareLinks");

            migrationBuilder.DropTable(
                name: "Shots");

            migrationBuilder.DropTable(
                name: "PlayerIbbaLinks");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "GameStats");

            migrationBuilder.DropTable(
                name: "IbbaTeams");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
