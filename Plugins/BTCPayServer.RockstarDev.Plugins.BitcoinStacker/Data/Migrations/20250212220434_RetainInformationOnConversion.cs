using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Migrations
{
    /// <inheritdoc />
    public partial class RetainInformationOnConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConversionRate",
                schema: "BTCPayServer.RockstarDev.Plugins.BitcoinStacker",
                table: "ExchangeOrders",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetAmount",
                schema: "BTCPayServer.RockstarDev.Plugins.BitcoinStacker",
                table: "ExchangeOrders",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConversionRate",
                schema: "BTCPayServer.RockstarDev.Plugins.BitcoinStacker",
                table: "ExchangeOrders");

            migrationBuilder.DropColumn(
                name: "TargetAmount",
                schema: "BTCPayServer.RockstarDev.Plugins.BitcoinStacker",
                table: "ExchangeOrders");
        }
    }
}
