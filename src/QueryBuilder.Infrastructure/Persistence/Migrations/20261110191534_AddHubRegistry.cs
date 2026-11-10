using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHubRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScopeKeys",
                table: "Apps",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AppReturnUrls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppReturnUrls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppReturnUrls_Apps_AppId",
                        column: x => x.AppId,
                        principalTable: "Apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppSigningKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Algorithm = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PublicKeyPem = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSigningKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppSigningKeys_Apps_AppId",
                        column: x => x.AppId,
                        principalTable: "Apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LaunchReplays",
                columns: table => new
                {
                    Jti = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AppId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaunchReplays", x => x.Jti);
                });

            migrationBuilder.CreateTable(
                name: "SeenUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    LastLaunchUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeenUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeenUsers_Apps_AppId",
                        column: x => x.AppId,
                        principalTable: "Apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppReturnUrls_AppId_Url",
                table: "AppReturnUrls",
                columns: new[] { "AppId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSigningKeys_AppId_Kid",
                table: "AppSigningKeys",
                columns: new[] { "AppId", "Kid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LaunchReplays_ExpiresUtc",
                table: "LaunchReplays",
                column: "ExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SeenUsers_AppId_UserId",
                table: "SeenUsers",
                columns: new[] { "AppId", "UserId" },
                unique: true);

            // Existing apps needed a value; new rows always name their keys, so the EF default goes. A named
            // database default stays outside the model for anything that inserts an app by hand.
            migrationBuilder.AlterColumn<string>(
                name: "ScopeKeys",
                table: "Apps",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValue: "");

            migrationBuilder.Sql("EXEC(N'ALTER TABLE [Apps] ADD CONSTRAINT [DF_Apps_ScopeKeys] DEFAULT '''' FOR [ScopeKeys]')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppReturnUrls");

            migrationBuilder.DropTable(
                name: "AppSigningKeys");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "LaunchReplays");

            migrationBuilder.DropTable(
                name: "SeenUsers");

            migrationBuilder.Sql("EXEC(N'IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = ''DF_Apps_ScopeKeys'') ALTER TABLE [Apps] DROP CONSTRAINT [DF_Apps_ScopeKeys]')");

            migrationBuilder.DropColumn(
                name: "ScopeKeys",
                table: "Apps");
        }
    }
}
