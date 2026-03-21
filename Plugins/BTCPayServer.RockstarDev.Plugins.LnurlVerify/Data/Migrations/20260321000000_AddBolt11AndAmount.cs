using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.LnurlVerify.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBolt11AndAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bolt11",
                schema: "BTCPayServer.RockstarDev.Plugins.LnurlVerify",
                table: "Invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AmountMilliSatoshi",
                schema: "BTCPayServer.RockstarDev.Plugins.LnurlVerify",
                table: "Invoices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bolt11",
                schema: "BTCPayServer.RockstarDev.Plugins.LnurlVerify",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "AmountMilliSatoshi",
                schema: "BTCPayServer.RockstarDev.Plugins.LnurlVerify",
                table: "Invoices");
        }
    }
}
