using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Migrations
{
    /// <inheritdoc />
    public partial class Net10ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                table: "ExchangeOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedForDate",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                table: "ExchangeOrders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedForDate",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                table: "ExchangeOrders");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                table: "ExchangeOrders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }
    }
}
