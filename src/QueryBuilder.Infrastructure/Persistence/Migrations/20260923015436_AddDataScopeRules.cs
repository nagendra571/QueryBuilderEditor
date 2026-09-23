using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataScopeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataScopeRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ColumnName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataScopeRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataScopeRules_DataSources_DataSourceId",
                        column: x => x.DataSourceId,
                        principalTable: "DataSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataScopeRules_DataSourceId_ObjectName_ScopeKey",
                table: "DataScopeRules",
                columns: new[] { "DataSourceId", "ObjectName", "ScopeKey" },
                unique: true,
                filter: "[ScopeKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataScopeRules");
        }
    }
}
