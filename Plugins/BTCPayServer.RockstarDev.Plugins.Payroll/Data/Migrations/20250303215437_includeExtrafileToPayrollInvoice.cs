using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Migrations
{
    /// <inheritdoc />
    public partial class includeExtrafileToPayrollInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtraFilenames",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraFilenames",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices");
        }
    }
}
