using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameToFeeBlockTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FeeRate",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepConfigurations",
                newName: "FeeBlockTarget");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FeeBlockTarget",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepConfigurations",
                newName: "FeeRate");
        }
    }
}
