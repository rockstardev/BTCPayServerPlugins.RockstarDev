﻿// <auto-generated />
using System;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Migrations
{
    [DbContext(typeof(PluginDbContext))]
    partial class PluginDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils")
                .HasAnnotation("ProductVersion", "8.0.11")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models.DbExchangeOrder", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<decimal>("Amount")
                        .HasColumnType("numeric");

                    b.Property<DateTimeOffset>("Created")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("CreatedBy")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<DateTimeOffset?>("CreatedForDate")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTimeOffset?>("DelayUntil")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Operation")
                        .HasColumnType("integer");

                    b.Property<int>("State")
                        .HasColumnType("integer");

                    b.Property<string>("StoreId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.HasKey("Id");

                    b.ToTable("ExchangeOrders", "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models.DbExchangeOrderLog", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Content")
                        .HasColumnType("text");

                    b.Property<DateTimeOffset>("Created")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Event")
                        .HasColumnType("integer");

                    b.Property<Guid>("ExchangeOrderId")
                        .HasColumnType("uuid");

                    b.Property<string>("Parameter")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.HasKey("Id");

                    b.HasIndex("ExchangeOrderId");

                    b.ToTable("ExchangeOrderLogs", "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models.DbSetting", b =>
                {
                    b.Property<string>("Key")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("StoreId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("Value")
                        .HasColumnType("text");

                    b.HasKey("Key");

                    b.ToTable("Settings", "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models.DbExchangeOrderLog", b =>
                {
                    b.HasOne("BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models.DbExchangeOrder", "ExchangeOrder")
                        .WithMany("ExchangeOrderLogs")
                        .HasForeignKey("ExchangeOrderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("ExchangeOrder");
                });

            modelBuilder.Entity("BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models.DbExchangeOrder", b =>
                {
                    b.Navigation("ExchangeOrderLogs");
                });
#pragma warning restore 612, 618
        }
    }
}