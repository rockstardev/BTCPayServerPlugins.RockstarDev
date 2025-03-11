using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Migrations
{
    /// <inheritdoc />
    public partial class IncludeEmailRemiderToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailReminder",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReminderSent",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailReminder",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollUsers");

            migrationBuilder.DropColumn(
                name: "LastReminderSent",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollUsers");
        }
    }
}
