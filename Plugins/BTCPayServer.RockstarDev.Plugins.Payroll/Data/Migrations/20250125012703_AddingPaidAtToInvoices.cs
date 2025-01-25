using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddingPaidAtToInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaidAt",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices");
        }
    }
}
