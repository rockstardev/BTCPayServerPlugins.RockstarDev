using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddingPayrollSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollSettings",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                columns: table => new
                {
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Setting = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollSettings", x => x.StoreId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollSettings",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll");
        }
    }
}
