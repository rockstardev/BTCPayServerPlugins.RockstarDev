using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddingFormIdFieldOnProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ReminderDays",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "Products",
                type: "character varying(25)",
                maxLength: 25,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(25)",
                oldMaxLength: 25);

            migrationBuilder.AddColumn<string>(
                name: "FormId",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "Products",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FormId",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "Products");

            migrationBuilder.AlterColumn<string>(
                name: "ReminderDays",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "Products",
                type: "character varying(25)",
                maxLength: 25,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(25)",
                oldMaxLength: 25,
                oldNullable: true);
        }
    }
}
