using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointMe.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddedInvoiceDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessLogoSnapshot",
                table: "Invoices",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessLogoSnapshot",
                table: "Invoices");
        }
    }
}
