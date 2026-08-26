using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceExtraAddressesDropUserDecoys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StonewallDecoyAddresses",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollUsers");

            migrationBuilder.AddColumn<string>(
                name: "ExtraAddresses",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraAddresses",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices");

            migrationBuilder.AddColumn<string>(
                name: "StonewallDecoyAddresses",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollUsers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }
    }
}
