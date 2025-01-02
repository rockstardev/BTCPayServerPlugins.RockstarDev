using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddingStoreIdProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                table: "ExchangeOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoreId",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                table: "ExchangeOrders");
        }
    }
}
