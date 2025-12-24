using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Services;

public class ExchangeOrderExport
{
    public string Process(IEnumerable<DbExchangeOrder> orders, string fileFormat)
    {
        var list = orders.Select(order => new ExportExchangeOrder
        {
            Id = order.Id.ToString(),
            Operation = order.Operation.ToString(),
            Amount = order.Amount,
            Created = order.Created,
            CreatedBy = order.CreatedBy,
            CreatedForDate = order.CreatedForDate,
            State = order.State.ToString(),
            TargetAmount = order.TargetAmount,
            ConversionRate = order.ConversionRate,
            DepositId = order.DepositId,
            DelayUntil = order.DelayUntil
        }).ToList();

        return fileFormat switch
        {
            "json" => ProcessJson(list),
            "csv" => ProcessCsv(list),
            _ => throw new Exception("Export format not supported")
        };
    }

    private static string ProcessJson(List<ExportExchangeOrder> orders)
    {
        var serializerSett = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
        var json = JsonConvert.SerializeObject(orders, Formatting.Indented, serializerSett);
        return json;
    }

    private static string ProcessCsv(IEnumerable<ExportExchangeOrder> orders)
    {
        using StringWriter writer = new();
        using var csvWriter = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture), true);
        csvWriter.Context.RegisterClassMap<ExportExchangeOrderMap>();
        csvWriter.WriteHeader<ExportExchangeOrder>();
        csvWriter.NextRecord();
        csvWriter.WriteRecords(orders);
        csvWriter.Flush();
        return writer.ToString();
    }
}

public sealed class ExportExchangeOrderMap : ClassMap<ExportExchangeOrder>
{
    public ExportExchangeOrderMap()
    {
        AutoMap(CultureInfo.InvariantCulture);
    }
}

public class ExportExchangeOrder
{
    [Name("Order Id")]
    public string Id { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    [Name("Amount (USD)")]
    public decimal Amount { get; set; }

    [Name("Created")]
    public DateTimeOffset Created { get; set; }

    [Name("Created By")]
    public string CreatedBy { get; set; } = string.Empty;

    [Name("Created For Date")]
    public DateTimeOffset? CreatedForDate { get; set; }

    public string State { get; set; } = string.Empty;

    [Name("Target Amount (BTC)")]
    public decimal? TargetAmount { get; set; }

    [Name("Conversion Rate")]
    public decimal? ConversionRate { get; set; }

    [Name("Deposit Id")]
    public string? DepositId { get; set; }

    [Name("Delay Until")]
    public DateTimeOffset? DelayUntil { get; set; }
}
