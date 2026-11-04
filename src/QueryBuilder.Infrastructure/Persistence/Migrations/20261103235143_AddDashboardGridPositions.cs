using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardGridPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoHeight",
                table: "DashboardTiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "X",
                table: "DashboardTiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Y",
                table: "DashboardTiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GridVersion",
                table: "Dashboards",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoHeight",
                table: "DashboardTiles");

            migrationBuilder.DropColumn(
                name: "X",
                table: "DashboardTiles");

            migrationBuilder.DropColumn(
                name: "Y",
                table: "DashboardTiles");

            migrationBuilder.DropColumn(
                name: "GridVersion",
                table: "Dashboards");
        }
    }
}
