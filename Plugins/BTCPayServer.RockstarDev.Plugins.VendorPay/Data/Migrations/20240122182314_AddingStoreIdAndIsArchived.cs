using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddingStoreIdAndIsArchived : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollUsers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoreId",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollUsers");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices");
        }
    }
}
