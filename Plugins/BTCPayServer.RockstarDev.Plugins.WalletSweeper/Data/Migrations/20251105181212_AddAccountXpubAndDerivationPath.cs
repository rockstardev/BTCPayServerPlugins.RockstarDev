using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountXpubAndDerivationPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DerivationPath",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "TrackedUtxos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountXpub",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepConfigurations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DerivationPath",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "TrackedUtxos");

            migrationBuilder.DropColumn(
                name: "AccountXpub",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepConfigurations");
        }
    }
}
