using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Migrations
{
    /// <inheritdoc />
    public partial class DepositIdOnExchangeOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DepositId",
                schema: "BTCPayServer.RockstarDev.Plugins.BitcoinStacker",
                table: "ExchangeOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepositId",
                schema: "BTCPayServer.RockstarDev.Plugins.BitcoinStacker",
                table: "ExchangeOrders");
        }
    }
}
