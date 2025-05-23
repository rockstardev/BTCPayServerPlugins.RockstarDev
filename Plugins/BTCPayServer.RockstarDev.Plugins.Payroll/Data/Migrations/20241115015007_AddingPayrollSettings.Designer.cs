﻿// <auto-generated />
using System;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Migrations
{
    [DbContext(typeof(PluginDbContext))]
    [Migration("20241115015007_AddingPayrollSettings")]
    partial class AddingPayrollSettings
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("BTCPayServer.RockstarDev.Plugins.Payroll")
                .HasAnnotation("ProductVersion", "8.0.7")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models.PayrollInvoice", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text");

                    b.Property<decimal>("Amount")
                        .HasColumnType("numeric");

                    b.Property<string>("BtcPaid")
                        .HasColumnType("text");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Currency")
                        .HasColumnType("text");

                    b.Property<string>("Description")
                        .HasColumnType("text");

                    b.Property<string>("Destination")
                        .HasColumnType("text");

                    b.Property<string>("InvoiceFilename")
                        .HasColumnType("text");

                    b.Property<bool>("IsArchived")
                        .HasColumnType("boolean");

                    b.Property<string>("PurchaseOrder")
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)");

                    b.Property<int>("State")
                        .HasColumnType("integer");

                    b.Property<string>("TxnId")
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("PayrollInvoices", "BTCPayServer.RockstarDev.Plugins.Payroll");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models.PayrollSetting", b =>
                {
                    b.Property<string>("StoreId")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("Setting")
                        .HasColumnType("text");

                    b.HasKey("StoreId");

                    b.ToTable("PayrollSettings", "BTCPayServer.RockstarDev.Plugins.Payroll");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models.PayrollUser", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text");

                    b.Property<string>("Email")
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.Property<string>("Password")
                        .HasColumnType("text");

                    b.Property<int>("State")
                        .HasColumnType("integer");

                    b.Property<string>("StoreId")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.HasKey("Id");

                    b.ToTable("PayrollUsers", "BTCPayServer.RockstarDev.Plugins.Payroll");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models.PayrollInvoice", b =>
                {
                    b.HasOne("BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models.PayrollUser", "User")
                        .WithMany("PayrollInvoices")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.Navigation("User");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models.PayrollUser", b =>
                {
                    b.Navigation("PayrollInvoices");
                });
#pragma warning restore 612, 618
        }
    }
}
