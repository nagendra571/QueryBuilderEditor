using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedQueryLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LayoutJson",
                table: "SavedQueries",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LayoutJson",
                table: "SavedQueries");
        }
    }
}
