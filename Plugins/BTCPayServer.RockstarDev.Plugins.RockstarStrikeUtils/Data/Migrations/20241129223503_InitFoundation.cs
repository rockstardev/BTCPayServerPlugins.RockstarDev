using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils");

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils");
        }
    }
}
