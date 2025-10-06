using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.RockstarDev.Plugins.WalletSweeper");

            migrationBuilder.CreateTable(
                name: "SweepConfigurations",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DestinationType = table.Column<int>(type: "integer", nullable: false),
                    DestinationValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MinimumBalance = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    MaximumBalance = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    ReserveAmount = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    LastSweepDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FeeRate = table.Column<int>(type: "integer", nullable: false),
                    EncryptedSeed = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SeedPassphrase = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SweepConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SweepHistories",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Fee = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Destination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TxId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SweepHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SweepHistories_SweepConfigurations_ConfigurationId",
                        column: x => x.ConfigurationId,
                        principalSchema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                        principalTable: "SweepConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SweepConfigurations_StoreId",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepConfigurations",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_SweepHistories_ConfigurationId",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepHistories",
                column: "ConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_SweepHistories_StoreId",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepHistories",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_SweepHistories_Timestamp",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepHistories",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SweepHistories",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper");

            migrationBuilder.DropTable(
                name: "SweepConfigurations",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper");
        }
    }
}
