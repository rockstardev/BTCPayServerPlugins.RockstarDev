using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddingFieldsForSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClickedAt",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "SubscriptionReminders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DebugAdditionalData",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "SubscriptionReminders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentRequestId",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "SubscriptionReminders",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClickedAt",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "SubscriptionReminders");

            migrationBuilder.DropColumn(
                name: "DebugAdditionalData",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "SubscriptionReminders");

            migrationBuilder.DropColumn(
                name: "PaymentRequestId",
                schema: "BTCPayServer.RockstarDev.Plugins.Subscriptions",
                table: "SubscriptionReminders");
        }
    }
}
