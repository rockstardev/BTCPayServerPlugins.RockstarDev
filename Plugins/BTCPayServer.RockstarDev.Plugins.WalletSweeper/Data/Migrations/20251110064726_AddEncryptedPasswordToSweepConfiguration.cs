using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptedPasswordToSweepConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptedPassword",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepConfigurations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedPassword",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepConfigurations");
        }
    }
}
