using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoreData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.RockstarDev.Plugins.Payroll");

            migrationBuilder.CreateTable(
                name: "PayrollUsers",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Password = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollInvoices",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    InvoiceFilename = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollInvoices_PayrollUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                        principalTable: "PayrollUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInvoices_UserId",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollInvoices",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll");

            migrationBuilder.DropTable(
                name: "PayrollUsers",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll");
        }
    }
}
