using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointMe.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "Appointments",
                type: "int",
                nullable: false,
                defaultValue: 30);

            // The real length of past bookings was never stored. The business's current slot length is
            // what the overlap check has been assuming for them, so behaviour doesn't change on upgrade.
            // Every appointment has a Business (FK since TenantForeignKeysAndPerTenantOrderNumber).
            migrationBuilder.Sql(@"
UPDATE a
SET a.[DurationMinutes] = b.[DefaultSlotMinutes]
FROM [Appointments] a
INNER JOIN [Businesses] b ON b.[Id] = a.[TenantId]
WHERE b.[DefaultSlotMinutes] > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Appointments");
        }
    }
}
