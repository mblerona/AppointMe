using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointMe.Repository.Migrations
{
    /// <summary>
    /// Batch 3, step 2 (audit S3, A6, A7):
    /// - S3: Appointment.OrderNumber is unique per tenant instead of globally.
    /// - A6: drops the unused shadow column Customers.BusinessId.
    /// - A7: foreign keys from Customers, Appointments, Invoices and AspNetUsers (TenantId, nullable for
    ///   users) to Businesses; ServiceOfferings/ServiceCategories no longer cascade. All are NO ACTION:
    ///   deleting a tenant will remove its data explicitly in a service.
    /// Before the foreign keys are added, every TenantId/BusinessId without a Business gets a placeholder
    /// Business ("Recovered business") with that Id, so no rows are lost.
    /// </summary>
    public partial class TenantForeignKeysAndPerTenantOrderNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO [Businesses]
    ([Id], [Name], [DefaultSlotMinutes], [CreatedAt], [UpdatedAt],
     [EnableServices], [EnableInvoices], [EnableStaffing], [EnablePayments],
     [WorkDayStart], [WorkDayEnd],
     [OpenMon], [OpenTue], [OpenWed], [OpenThu], [OpenFri], [OpenSat], [OpenSun])
SELECT t.[TenantId], N'Recovered business', 30, SYSUTCDATETIME(), SYSUTCDATETIME(),
       1, 0, 1, 1,
       '09:00:00', '17:00:00',
       1, 1, 1, 1, 1, 0, 0
FROM (
    SELECT [TenantId] FROM [Customers]
    UNION SELECT [TenantId] FROM [Appointments]
    UNION SELECT [TenantId] FROM [Invoices]
    UNION SELECT [TenantId] FROM [AspNetUsers] WHERE [TenantId] IS NOT NULL
    UNION SELECT [BusinessId] FROM [ServiceOfferings]
    UNION SELECT [BusinessId] FROM [ServiceCategories]
) AS t
WHERE NOT EXISTS (SELECT 1 FROM [Businesses] AS b WHERE b.[Id] = t.[TenantId]);");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Businesses_BusinessId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceCategories_Businesses_BusinessId",
                table: "ServiceCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceOfferings_Businesses_BusinessId",
                table: "ServiceOfferings");

            migrationBuilder.DropIndex(
                name: "IX_Customers_BusinessId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "UX_Appointment_OrderNumber",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "UX_Appointment_TenantId_OrderNumber",
                table: "Appointments",
                columns: new[] { "TenantId", "OrderNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Businesses_TenantId",
                table: "Appointments",
                column: "TenantId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Businesses_TenantId",
                table: "AspNetUsers",
                column: "TenantId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Businesses_TenantId",
                table: "Customers",
                column: "TenantId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Businesses_TenantId",
                table: "Invoices",
                column: "TenantId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceCategories_Businesses_BusinessId",
                table: "ServiceCategories",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceOfferings_Businesses_BusinessId",
                table: "ServiceOfferings",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old global unique index can't be restored while two tenants share an order number.
            // Fail with a clear message (the migration runs in a transaction, so nothing changes).
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Appointments] GROUP BY [OrderNumber] HAVING COUNT(*) > 1)
    THROW 50000, N'Cannot roll back TenantForeignKeysAndPerTenantOrderNumber: some order numbers are used by more than one tenant. Rename them first.', 1;");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Businesses_TenantId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Businesses_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Businesses_TenantId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Businesses_TenantId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceCategories_Businesses_BusinessId",
                table: "ServiceCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceOfferings_Businesses_BusinessId",
                table: "ServiceOfferings");

            migrationBuilder.DropIndex(
                name: "UX_Appointment_TenantId_OrderNumber",
                table: "Appointments");

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId",
                table: "Customers",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "UX_Appointment_OrderNumber",
                table: "Appointments",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Businesses_BusinessId",
                table: "Customers",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceCategories_Businesses_BusinessId",
                table: "ServiceCategories",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceOfferings_Businesses_BusinessId",
                table: "ServiceOfferings",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
