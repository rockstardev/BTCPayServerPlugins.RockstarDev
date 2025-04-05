using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Strike.Client.Balances;
using Strike.Client.CurrencyExchanges;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;

public class CurrencyExchangesViewModel
{
    public List<Balance> Balances { get; set; }

    public string Operation { get; set; }
    public decimal Amount { get; set; }

    public CurrencyExchangeQuote Quote { get; set; }
}

public class CurrencyExchangesCreateViewModel
{
    public string Sell { get; set; }

    [DisplayName("Sell Amount")]
    public decimal SellAmount { get; set; }

    public string Buy { get; set; }

    [DisplayName("Buy Amount")]
    [DisplayFormat(DataFormatString = "{0:F}", ApplyFormatInEditMode = true)]
    public decimal BuyAmount { get; set; }

    [DisplayName("Exchange Rate")]
    [DisplayFormat(DataFormatString = "{0:F}", ApplyFormatInEditMode = true)]
    public decimal ExchangeRate { get; set; }

    public Guid QuoteId { get; set; }
}
