﻿// <auto-generated />
using System;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Migrations
{
    [DbContext(typeof(PluginDbContext))]
    [Migration("20250312222152_AddingFormIdFieldOnProduct")]
    partial class AddingFormIdFieldOnProduct
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("BTCPayServer.RockstarDev.Plugins.Subscriptions")
                .HasAnnotation("ProductVersion", "8.0.11")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.Customer", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text");

                    b.Property<string>("Address1")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.Property<string>("Address2")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("City")
                        .IsRequired()
                        .HasMaxLength(85)
                        .HasColumnType("character varying(85)");

                    b.Property<string>("Country")
                        .IsRequired()
                        .HasMaxLength(56)
                        .HasColumnType("character varying(56)");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.Property<string>("StoreId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("ZipCode")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)");

                    b.HasKey("Id");

                    b.ToTable("Customers", "BTCPayServer.RockstarDev.Plugins.Subscriptions");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.PluginSetting", b =>
                {
                    b.Property<string>("StoreId")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("Key")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("Value")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("StoreId", "Key");

                    b.ToTable("PluginSettings", "BTCPayServer.RockstarDev.Plugins.Subscriptions");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.Product", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text");

                    b.Property<string>("Currency")
                        .IsRequired()
                        .HasMaxLength(4)
                        .HasColumnType("character varying(4)");

                    b.Property<int>("Duration")
                        .HasColumnType("integer");

                    b.Property<int>("DurationType")
                        .HasColumnType("integer");

                    b.Property<string>("FormId")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.Property<decimal>("Price")
                        .HasColumnType("numeric");

                    b.Property<string>("ReminderDays")
                        .HasMaxLength(25)
                        .HasColumnType("character varying(25)");

                    b.Property<string>("StoreId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.HasKey("Id");

                    b.ToTable("Products", "BTCPayServer.RockstarDev.Plugins.Subscriptions");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.Subscription", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text");

                    b.Property<DateTimeOffset>("Created")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("CustomerId")
                        .IsRequired()
                        .HasMaxLength(36)
                        .HasColumnType("character varying(36)");

                    b.Property<DateTimeOffset>("Expires")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("ExternalId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("PaymentRequestId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("ProductId")
                        .IsRequired()
                        .HasMaxLength(36)
                        .HasColumnType("character varying(36)");

                    b.Property<int>("State")
                        .HasMaxLength(10)
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("CustomerId");

                    b.HasIndex("ProductId");

                    b.ToTable("Subscriptions", "BTCPayServer.RockstarDev.Plugins.Subscriptions");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.SubscriptionReminder", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text");

                    b.Property<DateTimeOffset?>("ClickedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTimeOffset>("Created")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("DebugAdditionalData")
                        .HasColumnType("text");

                    b.Property<string>("PaymentRequestId")
                        .HasMaxLength(36)
                        .HasColumnType("character varying(36)");

                    b.Property<string>("SubscriptionId")
                        .IsRequired()
                        .HasMaxLength(36)
                        .HasColumnType("character varying(36)");

                    b.HasKey("Id");

                    b.HasIndex("SubscriptionId");

                    b.ToTable("SubscriptionReminders", "BTCPayServer.RockstarDev.Plugins.Subscriptions");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.Subscription", b =>
                {
                    b.HasOne("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.Customer", "Customer")
                        .WithMany("Subscriptions")
                        .HasForeignKey("CustomerId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.Product", "Product")
                        .WithMany("Subscriptions")
                        .HasForeignKey("ProductId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Customer");

                    b.Navigation("Product");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.SubscriptionReminder", b =>
                {
                    b.HasOne("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.Subscription", "Subscription")
                        .WithMany("SubscriptionReminders")
                        .HasForeignKey("SubscriptionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Subscription");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.Customer", b =>
                {
                    b.Navigation("Subscriptions");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.Product", b =>
                {
                    b.Navigation("Subscriptions");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models.Subscription", b =>
                {
                    b.Navigation("SubscriptionReminders");
                });
#pragma warning restore 612, 618
        }
    }
}
