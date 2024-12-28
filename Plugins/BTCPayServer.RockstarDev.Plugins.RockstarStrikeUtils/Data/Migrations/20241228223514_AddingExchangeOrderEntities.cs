using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddingExchangeOrderEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExchangeOrderLogs",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExchangeOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Parameters = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeOrderLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeOrders",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DelayUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Executed = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CostBasis = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeOrders", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeOrderLogs",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils");

            migrationBuilder.DropTable(
                name: "ExchangeOrders",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils");
        }
    }
}
