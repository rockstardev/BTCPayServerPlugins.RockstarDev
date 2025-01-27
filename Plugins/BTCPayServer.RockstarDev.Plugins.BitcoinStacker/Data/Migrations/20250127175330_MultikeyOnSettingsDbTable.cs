using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultikeyOnSettingsDbTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Settings",
                schema: "BTCPayServer.RockstarDev.Plugins.BitcoinStacker",
                table: "Settings");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Settings",
                schema: "BTCPayServer.RockstarDev.Plugins.BitcoinStacker",
                table: "Settings",
                columns: new[] { "StoreId", "Key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Settings",
                schema: "BTCPayServer.RockstarDev.Plugins.BitcoinStacker",
                table: "Settings");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Settings",
                schema: "BTCPayServer.RockstarDev.Plugins.BitcoinStacker",
                table: "Settings",
                column: "Key");
        }
    }
}
