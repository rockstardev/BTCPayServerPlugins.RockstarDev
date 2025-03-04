using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Migrations
{
    /// <inheritdoc />
    public partial class FoundationForSubscriptionsDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.RockstarDev.Plugins.Subscriptions");

            migrationBuilder.CreateTable(
                name: "Customers",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Address1 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Address2 = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    City = table.Column<string>(type: "character varying(85)", maxLength: 85, nullable: false),
                    Country = table.Column<string>(type: "character varying(56)", maxLength: 56, nullable: false),
                    ZipCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PluginSettings",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginSettings", x => new { x.StoreId, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "Products",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Duration = table.Column<int>(type: "integer", nullable: false),
                    DurationType = table.Column<int>(type: "integer", maxLength: 10, nullable: false),
                    ReminderDays = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ProductId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Expires = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    State = table.Column<int>(type: "integer", maxLength: 10, nullable: false),
                    PaymentRequestId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionReminders",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionReminders_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalSchema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionReminders_SubscriptionId",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "SubscriptionReminders",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_CustomerId",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "Subscriptions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ProductId",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "Subscriptions",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PluginSettings",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions");

            migrationBuilder.DropTable(
                name: "SubscriptionReminders",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions");

            migrationBuilder.DropTable(
                name: "Subscriptions",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions");

            migrationBuilder.DropTable(
                name: "Customers",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions");

            migrationBuilder.DropTable(
                name: "Products",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions");
        }
    }
}
