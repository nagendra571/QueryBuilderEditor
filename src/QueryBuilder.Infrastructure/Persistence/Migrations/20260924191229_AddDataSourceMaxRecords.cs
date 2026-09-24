using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataSourceMaxRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxRecords",
                table: "DataSources",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxRecords",
                table: "DataSources");
        }
    }
}
