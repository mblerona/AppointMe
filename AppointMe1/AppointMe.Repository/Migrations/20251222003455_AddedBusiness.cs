using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointMe.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddedBusiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OpenFri",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OpenMon",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OpenSat",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OpenSun",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OpenThu",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OpenTue",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OpenWed",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "WorkDayEnd",
                table: "Businesses",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "WorkDayStart",
                table: "Businesses",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpenFri",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "OpenMon",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "OpenSat",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "OpenSun",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "OpenThu",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "OpenTue",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "OpenWed",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "WorkDayEnd",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "WorkDayStart",
                table: "Businesses");
        }
    }
}
