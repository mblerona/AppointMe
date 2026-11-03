using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointMe.Repository.Migrations
{
    /// <summary>
    /// Data-only migration (no schema change) for Batch 2, step 2:
    /// 1. Email confirmation is now required, so every user that existed before it is marked as confirmed.
    /// 2. Settings/Business GET no longer creates a Business, so every user gets one now:
    ///    users without a TenantId get a new one, and every TenantId without a Business row gets a
    ///    Business with that Id (same defaults the old GET used).
    /// Every statement is idempotent; running it on an empty database does nothing.
    /// </summary>
    public partial class ConfirmExistingUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE [AspNetUsers]
SET [EmailConfirmed] = 1
WHERE [EmailConfirmed] = 0;");

            migrationBuilder.Sql(@"
UPDATE [AspNetUsers]
SET [TenantId] = NEWID(), [UpdatedAt] = SYSUTCDATETIME()
WHERE [TenantId] IS NULL OR [TenantId] = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.Sql(@"
INSERT INTO [Businesses]
    ([Id], [Name], [DefaultSlotMinutes], [CreatedAt], [UpdatedAt],
     [EnableServices], [EnableInvoices], [EnableStaffing], [EnablePayments],
     [WorkDayStart], [WorkDayEnd],
     [OpenMon], [OpenTue], [OpenWed], [OpenThu], [OpenFri], [OpenSat], [OpenSun])
SELECT t.[TenantId], N'My Business', 30, SYSUTCDATETIME(), SYSUTCDATETIME(),
       1, 0, 1, 1,
       '09:00:00', '17:00:00',
       1, 1, 1, 1, 1, 0, 0
FROM (SELECT DISTINCT [TenantId] FROM [AspNetUsers] WHERE [TenantId] IS NOT NULL) AS t
WHERE NOT EXISTS (SELECT 1 FROM [Businesses] AS b WHERE b.[Id] = t.[TenantId]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the previous confirmation state is not recorded, and the created
            // Businesses may already hold data, so this migration can't be undone safely.
        }
    }
}
