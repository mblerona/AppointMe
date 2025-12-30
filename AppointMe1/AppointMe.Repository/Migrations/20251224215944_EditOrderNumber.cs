using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointMe.Repository.Migrations
{
    /// <inheritdoc />
    public partial class EditOrderNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointment_OrderNumber",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "UX_Appointment_OrderNumber",
                table: "Appointments",
                column: "OrderNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Appointment_OrderNumber",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointment_OrderNumber",
                table: "Appointments",
                column: "OrderNumber");
        }
    }
}
