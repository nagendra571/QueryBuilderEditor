using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Apps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataSettings", x => x.Key);
                });

            // A literal, not App.DefaultId: a migration must keep meaning what it meant when it was written.
            migrationBuilder.InsertData(
                table: "Apps",
                columns: new[] { "Id", "Key", "Name", "IsEnabled", "CreatedAtUtc" },
                values: new object[] { new Guid("00000000-0000-0000-0000-00000000000d"), "default", "Default", true, new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero) });

            migrationBuilder.AddColumn<Guid>(
                name: "AppId",
                table: "SavedQueries",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AddColumn<Guid>(
                name: "AppId",
                table: "QueryVisualizations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AddColumn<Guid>(
                name: "AppId",
                table: "QueryShares",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AddColumn<Guid>(
                name: "AppId",
                table: "DataSources",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AddColumn<Guid>(
                name: "AppId",
                table: "DataScopeRules",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AddColumn<Guid>(
                name: "AppId",
                table: "DashboardTiles",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AddColumn<Guid>(
                name: "AppId",
                table: "DashboardShares",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AddColumn<Guid>(
                name: "AppId",
                table: "Dashboards",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AddColumn<Guid>(
                name: "AppId",
                table: "AuditLogEntries",
                type: "uniqueidentifier",
                nullable: true);

            // Existing history belongs to the Default app, or the per-app audit filter would hide all of it.
            migrationBuilder.Sql("EXEC(N'UPDATE [AuditLogEntries] SET [AppId] = ''00000000-0000-0000-0000-00000000000d'' WHERE [AppId] IS NULL')");

            migrationBuilder.CreateIndex(
                name: "IX_SavedQueries_AppId_OwnerId",
                table: "SavedQueries",
                columns: new[] { "AppId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_QueryVisualizations_AppId",
                table: "QueryVisualizations",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryShares_AppId",
                table: "QueryShares",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_DataSources_AppId",
                table: "DataSources",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_DataScopeRules_AppId",
                table: "DataScopeRules",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardTiles_AppId",
                table: "DashboardTiles",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardShares_AppId",
                table: "DashboardShares",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_Dashboards_AppId_OwnerId",
                table: "Dashboards",
                columns: new[] { "AppId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_AppId_TimestampUtc",
                table: "AuditLogEntries",
                columns: new[] { "AppId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Apps_Key",
                table: "Apps",
                column: "Key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogEntries_Apps_AppId",
                table: "AuditLogEntries",
                column: "AppId",
                principalTable: "Apps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Dashboards_Apps_AppId",
                table: "Dashboards",
                column: "AppId",
                principalTable: "Apps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DashboardShares_Apps_AppId",
                table: "DashboardShares",
                column: "AppId",
                principalTable: "Apps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DashboardTiles_Apps_AppId",
                table: "DashboardTiles",
                column: "AppId",
                principalTable: "Apps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DataScopeRules_Apps_AppId",
                table: "DataScopeRules",
                column: "AppId",
                principalTable: "Apps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DataSources_Apps_AppId",
                table: "DataSources",
                column: "AppId",
                principalTable: "Apps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QueryShares_Apps_AppId",
                table: "QueryShares",
                column: "AppId",
                principalTable: "Apps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QueryVisualizations_Apps_AppId",
                table: "QueryVisualizations",
                column: "AppId",
                principalTable: "Apps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SavedQueries_Apps_AppId",
                table: "SavedQueries",
                column: "AppId",
                principalTable: "Apps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // New rows must name their app (the save stamp does); only existing rows needed the default.
            migrationBuilder.AlterColumn<Guid>(
                name: "AppId",
                table: "SavedQueries",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AlterColumn<Guid>(
                name: "AppId",
                table: "QueryVisualizations",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AlterColumn<Guid>(
                name: "AppId",
                table: "QueryShares",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AlterColumn<Guid>(
                name: "AppId",
                table: "DataSources",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AlterColumn<Guid>(
                name: "AppId",
                table: "DataScopeRules",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AlterColumn<Guid>(
                name: "AppId",
                table: "DashboardTiles",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AlterColumn<Guid>(
                name: "AppId",
                table: "DashboardShares",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            migrationBuilder.AlterColumn<Guid>(
                name: "AppId",
                table: "Dashboards",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-00000000000d"));

            // Named database defaults kept outside the EF model: embedded consumers register sources with a raw
            // INSERT that omits AppId, and a 1.5.0 app sharing the database (or a rollback) can't name an app.
            // EF always sends AppId itself, so the save guard is still what stamps new rows.
            foreach (var table in AppOwnedTables)
            {
                migrationBuilder.Sql($"EXEC(N'ALTER TABLE [{table}] ADD CONSTRAINT [DF_{table}_AppId] DEFAULT ''00000000-0000-0000-0000-00000000000d'' FOR [AppId]')");
            }
        }

        private static readonly string[] AppOwnedTables =
        [
            "SavedQueries", "QueryVisualizations", "QueryShares", "DataSources", "DataScopeRules",
            "DashboardTiles", "DashboardShares", "Dashboards", "AuditLogEntries",
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in AppOwnedTables)
            {
                migrationBuilder.Sql($"EXEC(N'IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = ''DF_{table}_AppId'') ALTER TABLE [{table}] DROP CONSTRAINT [DF_{table}_AppId]')");
            }

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogEntries_Apps_AppId",
                table: "AuditLogEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Dashboards_Apps_AppId",
                table: "Dashboards");

            migrationBuilder.DropForeignKey(
                name: "FK_DashboardShares_Apps_AppId",
                table: "DashboardShares");

            migrationBuilder.DropForeignKey(
                name: "FK_DashboardTiles_Apps_AppId",
                table: "DashboardTiles");

            migrationBuilder.DropForeignKey(
                name: "FK_DataScopeRules_Apps_AppId",
                table: "DataScopeRules");

            migrationBuilder.DropForeignKey(
                name: "FK_DataSources_Apps_AppId",
                table: "DataSources");

            migrationBuilder.DropForeignKey(
                name: "FK_QueryShares_Apps_AppId",
                table: "QueryShares");

            migrationBuilder.DropForeignKey(
                name: "FK_QueryVisualizations_Apps_AppId",
                table: "QueryVisualizations");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedQueries_Apps_AppId",
                table: "SavedQueries");

            migrationBuilder.DropIndex(
                name: "IX_SavedQueries_AppId_OwnerId",
                table: "SavedQueries");

            migrationBuilder.DropIndex(
                name: "IX_QueryVisualizations_AppId",
                table: "QueryVisualizations");

            migrationBuilder.DropIndex(
                name: "IX_QueryShares_AppId",
                table: "QueryShares");

            migrationBuilder.DropIndex(
                name: "IX_DataSources_AppId",
                table: "DataSources");

            migrationBuilder.DropIndex(
                name: "IX_DataScopeRules_AppId",
                table: "DataScopeRules");

            migrationBuilder.DropIndex(
                name: "IX_DashboardTiles_AppId",
                table: "DashboardTiles");

            migrationBuilder.DropIndex(
                name: "IX_DashboardShares_AppId",
                table: "DashboardShares");

            migrationBuilder.DropIndex(
                name: "IX_Dashboards_AppId_OwnerId",
                table: "Dashboards");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogEntries_AppId_TimestampUtc",
                table: "AuditLogEntries");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "SavedQueries");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "QueryVisualizations");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "QueryShares");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "DataSources");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "DataScopeRules");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "DashboardTiles");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "DashboardShares");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "Dashboards");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "AuditLogEntries");

            migrationBuilder.DropTable(
                name: "Apps");

            migrationBuilder.DropTable(
                name: "MetadataSettings");
        }
    }
}
