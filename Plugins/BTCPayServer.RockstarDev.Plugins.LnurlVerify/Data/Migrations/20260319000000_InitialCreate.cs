using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.LnurlVerify.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.RockstarDev.Plugins.LnurlVerify");

            migrationBuilder.CreateTable(
                name: "Invoices",
                schema: "BTCPayServer.RockstarDev.Plugins.LnurlVerify",
                columns: table => new
                {
                    PaymentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InvoiceId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VerifyUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.PaymentHash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceId",
                schema: "BTCPayServer.RockstarDev.Plugins.LnurlVerify",
                table: "Invoices",
                column: "InvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Invoices",
                schema: "BTCPayServer.RockstarDev.Plugins.LnurlVerify");
        }
    }
}
