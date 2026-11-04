using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointMe.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceYearAndSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InvoiceSequence",
                table: "Invoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceYear",
                table: "Invoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Numbers in the usual form keep their year and sequence. The invoice number itself never
            // changes, because customers have already received it.
            migrationBuilder.Sql(@"
WITH parsed AS (
    SELECT Id, TenantId, IssuedAt,
           TRY_CAST(SUBSTRING(InvoiceNumber, 5, 4) AS int) AS ParsedYear,
           TRY_CAST(SUBSTRING(InvoiceNumber, 10, 40) AS int) AS ParsedSequence
    FROM Invoices
    WHERE InvoiceNumber LIKE 'INV-[0-9][0-9][0-9][0-9]-[0-9]%'
      AND SUBSTRING(InvoiceNumber, 10, 40) NOT LIKE '%[^0-9]%'
),
ranked AS (
    SELECT Id, ParsedYear, ParsedSequence,
           ROW_NUMBER() OVER (PARTITION BY TenantId, ParsedYear, ParsedSequence ORDER BY IssuedAt, Id) AS Occurrence
    FROM parsed
    WHERE ParsedYear > 0 AND ParsedSequence IS NOT NULL
)
UPDATE i
SET i.InvoiceYear = r.ParsedYear, i.InvoiceSequence = r.ParsedSequence
FROM Invoices i
INNER JOIN ranked r ON r.Id = i.Id
WHERE r.Occurrence = 1;");

            // The rest can't keep a number of their own: malformed ones, and duplicates by value such as
            // INV-2026-1 next to INV-2026-0001. They continue after the highest sequence of their year,
            // so that the unique index below can be created and no later invoice reuses a number.
            migrationBuilder.Sql(@"
WITH pending AS (
    SELECT Id, TenantId, YEAR(IssuedAt) AS IssuedYear,
           ROW_NUMBER() OVER (PARTITION BY TenantId, YEAR(IssuedAt) ORDER BY IssuedAt, Id) AS Position
    FROM Invoices
    WHERE InvoiceYear = 0
)
UPDATE i
SET i.InvoiceYear = p.IssuedYear,
    i.InvoiceSequence = p.Position + ISNULL(
        (SELECT MAX(x.InvoiceSequence) FROM Invoices x WHERE x.TenantId = p.TenantId AND x.InvoiceYear = p.IssuedYear), 0)
FROM Invoices i
INNER JOIN pending p ON p.Id = i.Id;");

            migrationBuilder.CreateIndex(
                name: "UX_Invoice_TenantId_Year_Sequence",
                table: "Invoices",
                columns: new[] { "TenantId", "InvoiceYear", "InvoiceSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Invoice_TenantId_Year_Sequence",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceSequence",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceYear",
                table: "Invoices");
        }
    }
}
