using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointMe.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessSuspensionAndActionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedAt",
                table: "Businesses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionNote",
                table: "Businesses",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SuspensionReason",
                table: "Businesses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminActionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AdminEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChangedFields = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminActionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionLog_At",
                table: "AdminActionLogs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionLog_BusinessId_At",
                table: "AdminActionLogs",
                columns: new[] { "BusinessId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminActionLogs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "SuspendedAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "SuspensionNote",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "SuspensionReason",
                table: "Businesses");
        }
    }
}
