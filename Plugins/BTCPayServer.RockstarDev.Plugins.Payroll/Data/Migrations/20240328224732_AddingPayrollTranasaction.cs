using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddingPayrollTranasaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollTransactions",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Recipient = table.Column<string>(type: "text", nullable: true),
                    InvoiceId = table.Column<string>(type: "text", nullable: true),
                    Link = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    BtcUsdRate = table.Column<decimal>(type: "numeric", nullable: true),
                    BtcJpyRate = table.Column<decimal>(type: "numeric", nullable: true),
                    BtcAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    Balance = table.Column<string>(type: "text", nullable: true),
                    MinerFee = table.Column<decimal>(type: "numeric", nullable: true),
                    TransactionId = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollTransactions", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollTransactions",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll");
        }
    }
}
