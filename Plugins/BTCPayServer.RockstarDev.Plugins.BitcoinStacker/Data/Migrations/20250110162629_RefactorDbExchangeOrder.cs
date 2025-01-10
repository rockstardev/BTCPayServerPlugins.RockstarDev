using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefactorDbExchangeOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils");

            migrationBuilder.CreateTable(
                name: "ExchangeOrders",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DelayUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedForDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeOrderLogs",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExchangeOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Event = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Parameter = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeOrderLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExchangeOrderLogs_ExchangeOrders_ExchangeOrderId",
                        column: x => x.ExchangeOrderId,
                        principalSchema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                        principalTable: "ExchangeOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeOrderLogs_ExchangeOrderId",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils",
                table: "ExchangeOrderLogs",
                column: "ExchangeOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeOrderLogs",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils");

            migrationBuilder.DropTable(
                name: "ExchangeOrders",
                schema: "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils");
        }
    }
}
