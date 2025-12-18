using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefactoringPayrollInvoiceMaxLengthAddingNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TxnId",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceFilename",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Destination",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BtcPaid",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminNote",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminNote",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(36)",
                oldMaxLength: 36,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TxnId",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceFilename",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(36)",
                oldMaxLength: 36,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Destination",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(5)",
                oldMaxLength: 5,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BtcPaid",
                schema: "BTCPayServer.RockstarDev.Plugins.Payroll",
                table: "PayrollInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
