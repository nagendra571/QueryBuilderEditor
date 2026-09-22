using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogScopeAndAllowedObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CatalogScope",
                table: "DataSources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE DataSources SET CatalogScope = CASE WHEN ViewsOnly = 1 THEN 0 ELSE 2 END");

            migrationBuilder.DropColumn(
                name: "ViewsOnly",
                table: "DataSources");

            migrationBuilder.AddColumn<string>(
                name: "AllowedObjects",
                table: "DataSources",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedObjects",
                table: "DataSources");

            migrationBuilder.AddColumn<bool>(
                name: "ViewsOnly",
                table: "DataSources",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("UPDATE DataSources SET ViewsOnly = CASE WHEN CatalogScope = 0 THEN 1 ELSE 0 END");

            migrationBuilder.DropColumn(
                name: "CatalogScope",
                table: "DataSources");
        }
    }
}
