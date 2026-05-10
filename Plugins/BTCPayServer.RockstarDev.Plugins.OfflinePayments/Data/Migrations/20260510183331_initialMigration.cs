using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.Data.Migrations
{
    /// <inheritdoc />
    public partial class initialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.RockstarDev.Plugins.OfflinePayment");

            migrationBuilder.CreateTable(
                name: "OfflineMethodConfigs",
                schema: "BTCPayServer.RockstarDev.Plugins.OfflinePayment",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    MethodId = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    Instructions = table.Column<string>(type: "text", nullable: true),
                    BankName = table.Column<string>(type: "text", nullable: true),
                    BankAddress = table.Column<string>(type: "text", nullable: true),
                    AccountName = table.Column<string>(type: "text", nullable: true),
                    AccountAddress = table.Column<string>(type: "text", nullable: true),
                    RoutingNumber = table.Column<string>(type: "text", nullable: true),
                    AccountNumber = table.Column<string>(type: "text", nullable: true),
                    ReferenceTemplate = table.Column<string>(type: "text", nullable: true),
                    EstimatedSettlementTime = table.Column<string>(type: "text", nullable: true),
                    SupportContact = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflineMethodConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfflinePendingPayments",
                schema: "BTCPayServer.RockstarDev.Plugins.OfflinePayment",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    InvoiceId = table.Column<string>(type: "text", nullable: true),
                    MethodId = table.Column<string>(type: "text", nullable: true),
                    ResolvedReference = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CustomerNote = table.Column<string>(type: "text", nullable: true),
                    CustomerMarkedSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AdminConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AdminInvalidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemittanceFileUrl = table.Column<string>(type: "text", nullable: true),
                    AdminUserId = table.Column<string>(type: "text", nullable: true),
                    MethodConfigId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflinePendingPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfflinePendingPayments_OfflineMethodConfigs_MethodConfigId",
                        column: x => x.MethodConfigId,
                        principalSchema: "BTCPayServer.RockstarDev.Plugins.OfflinePayment",
                        principalTable: "OfflineMethodConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineMethodConfigs_StoreId_MethodId",
                schema: "BTCPayServer.RockstarDev.Plugins.OfflinePayment",
                table: "OfflineMethodConfigs",
                columns: new[] { "StoreId", "MethodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfflinePendingPayments_MethodConfigId",
                schema: "BTCPayServer.RockstarDev.Plugins.OfflinePayment",
                table: "OfflinePendingPayments",
                column: "MethodConfigId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfflinePendingPayments",
                schema: "BTCPayServer.RockstarDev.Plugins.OfflinePayment");

            migrationBuilder.DropTable(
                name: "OfflineMethodConfigs",
                schema: "BTCPayServer.RockstarDev.Plugins.OfflinePayment");
        }
    }
}
