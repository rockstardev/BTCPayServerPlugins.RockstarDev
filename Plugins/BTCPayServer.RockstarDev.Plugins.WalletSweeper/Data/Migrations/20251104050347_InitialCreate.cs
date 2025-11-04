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
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConfigName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EncryptedSeed = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DerivationPath = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AddressGapLimit = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumBalance = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    MaximumBalance = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    IntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    ReserveAmount = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    FeeRate = table.Column<int>(type: "integer", nullable: false),
                    DestinationType = table.Column<int>(type: "integer", nullable: false),
                    DestinationAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AutoGenerateLabel = table.Column<bool>(type: "boolean", nullable: false),
                    LastMonitored = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSwept = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
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
                    Id = table.Column<string>(type: "text", nullable: false),
                    SweepConfigurationId = table.Column<string>(type: "text", nullable: false),
                    SweepDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TransactionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Fee = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    DestinationAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    WeightedAverageCostBasis = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    UtxoCount = table.Column<int>(type: "integer", nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SweepHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SweepHistories_SweepConfigurations_SweepConfigurationId",
                        column: x => x.SweepConfigurationId,
                        principalSchema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                        principalTable: "SweepConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackedUtxos",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SweepConfigurationId = table.Column<string>(type: "text", nullable: false),
                    Outpoint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TxId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Vout = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Confirmations = table.Column<int>(type: "integer", nullable: false),
                    CostBasisUsd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ReceivedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CostBasisSource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsSpent = table.Column<bool>(type: "boolean", nullable: false),
                    SpentDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SpentInSweepTxId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedUtxos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedUtxos_SweepConfigurations_SweepConfigurationId",
                        column: x => x.SweepConfigurationId,
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
                name: "IX_SweepConfigurations_StoreId_ConfigName",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepConfigurations",
                columns: new[] { "StoreId", "ConfigName" });

            migrationBuilder.CreateIndex(
                name: "IX_SweepHistories_SweepConfigurationId",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepHistories",
                column: "SweepConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_SweepHistories_SweepDate",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "SweepHistories",
                column: "SweepDate");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedUtxos_IsSpent",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "TrackedUtxos",
                column: "IsSpent");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedUtxos_Outpoint",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "TrackedUtxos",
                column: "Outpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedUtxos_SweepConfigurationId",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "TrackedUtxos",
                column: "SweepConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedUtxos_SweepConfigurationId_IsSpent",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper",
                table: "TrackedUtxos",
                columns: new[] { "SweepConfigurationId", "IsSpent" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SweepHistories",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper");

            migrationBuilder.DropTable(
                name: "TrackedUtxos",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper");

            migrationBuilder.DropTable(
                name: "SweepConfigurations",
                schema: "BTCPayServer.RockstarDev.Plugins.WalletSweeper");
        }
    }
}
