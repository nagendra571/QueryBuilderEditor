using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHubRolesAndPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LockedBySuperAdmin",
                table: "DataSources",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAtUtc",
                table: "Dashboards",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishedBy",
                table: "Dashboards",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PublishedToApp",
                table: "Dashboards",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AppAdminsCanManageConnections",
                table: "Apps",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxRecords",
                table: "Apps",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Apps",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockedBySuperAdmin",
                table: "DataSources");

            migrationBuilder.DropColumn(
                name: "PublishedAtUtc",
                table: "Dashboards");

            migrationBuilder.DropColumn(
                name: "PublishedBy",
                table: "Dashboards");

            migrationBuilder.DropColumn(
                name: "PublishedToApp",
                table: "Dashboards");

            migrationBuilder.DropColumn(
                name: "AppAdminsCanManageConnections",
                table: "Apps");

            migrationBuilder.DropColumn(
                name: "MaxRecords",
                table: "Apps");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Apps");
        }
    }
}
