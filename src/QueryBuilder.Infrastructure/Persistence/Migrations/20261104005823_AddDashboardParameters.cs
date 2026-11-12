using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParameterMappingsJson",
                table: "DashboardTiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParametersJson",
                table: "Dashboards",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParameterMappingsJson",
                table: "DashboardTiles");

            migrationBuilder.DropColumn(
                name: "ParametersJson",
                table: "Dashboards");
        }
    }
}
