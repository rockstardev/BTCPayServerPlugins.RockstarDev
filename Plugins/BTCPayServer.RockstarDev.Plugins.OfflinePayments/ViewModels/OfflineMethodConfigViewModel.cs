using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Data;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.ViewModels;


public class OfflineMethodConfigViewModel
{
    public string? Id { get; set; }
    public string StoreId { get; set; }

    [Required]
    [MaxLength(50)]
    [Display(Name = "Method ID")]
    public string MethodId { get; set; } = string.Empty; // ACH, WIRE, CHECK, CASH

    [Required]
    [MaxLength(100)]
    [Display(Name = "Display Name")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "Payment Instructions")]
    [MaxLength(50)]
    public string? Instructions { get; set; }

    [Display(Name = "Bank Name")]
    public string? BankName { get; set; }

    [Display(Name = "Bank Address")]
    public string? BankAddress { get; set; }

    [Display(Name = "Account Name")]
    public string? AccountName { get; set; }

    [Display(Name = "Account Address")]
    public string? AccountAddress { get; set; }

    [Display(Name = "Routing Number")]
    public string? RoutingNumber { get; set; }

    [Display(Name = "Account Number")]
    public string? AccountNumber { get; set; }

    [Display(Name = "Reference / Memo Template")]
    public string ReferenceTemplate { get; set; } = "Invoice {InvoiceId}";

    [Display(Name = "Estimated Settlement Time")]
    public string? EstimatedSettlementTime { get; set; }

    [Display(Name = "Support Contact")]
    public string? SupportContact { get; set; }

    [Display(Name = "Require manual confirmation before settling invoice")]
    public bool RequiresManualConfirmation { get; set; } = true;

    [Display(Name = "Enabled")]
    public bool IsEnabled { get; set; } = true;

    public int SortOrder { get; set; } = 0;
    public List<string> AvailableMethodTypes { get; set; } = new();

    public static OfflineMethodConfigViewModel FromModel(OfflineMethodConfig m) => new()
    {
        Id = m.Id,
        MethodId = m.MethodId,
        DisplayName = m.DisplayName,
        Instructions = m.Instructions,
        BankName = m.BankName,
        BankAddress = m.BankAddress,
        AccountName = m.AccountName,
        AccountAddress = m.AccountAddress,
        RoutingNumber = m.RoutingNumber,
        AccountNumber = m.AccountNumber,
        ReferenceTemplate = m.ReferenceTemplate,
        EstimatedSettlementTime = m.EstimatedSettlementTime,
        SupportContact = m.SupportContact,
        RequiresManualConfirmation = m.RequiresManualConfirmation,
        IsEnabled = m.IsEnabled,
        SortOrder = m.SortOrder
    };

    public OfflineMethodConfig ToModel(string storeId) => new()
    {
        Id = Id ?? Guid.NewGuid().ToString(),
        StoreId = storeId,
        MethodId = MethodId.ToUpperInvariant().Trim(),
        DisplayName = DisplayName,
        Instructions = Instructions,
        BankName = BankName,
        BankAddress = BankAddress,
        AccountName = AccountName,
        AccountAddress = AccountAddress,
        RoutingNumber = RoutingNumber,
        AccountNumber = AccountNumber,
        ReferenceTemplate = string.IsNullOrWhiteSpace(ReferenceTemplate)
            ? "Invoice {InvoiceId}"
            : ReferenceTemplate,
        EstimatedSettlementTime = EstimatedSettlementTime,
        SupportContact = SupportContact,
        RequiresManualConfirmation = RequiresManualConfirmation,
        IsEnabled = IsEnabled,
        SortOrder = SortOrder
    };
}

public class OfflineSettingsViewModel
{
    public List<OfflineMethodConfig> Methods { get; set; } = new();
    public string StoreId { get; set; } = string.Empty;
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

// ── Pending queue ─────────────────────────────────────────────────────────────

public class OfflinePendingQueueViewModel
{
    public string StoreId { get; set; } = string.Empty;
    public List<OfflinePendingPayment> Items { get; set; } = new();
    public List<string> AvailableMethodIds { get; set; } = new();
    public string? MethodIdFilter { get; set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

// ── Customer checkout ─────────────────────────────────────────────────────────

public class OfflineCheckoutViewModel
{
    public string InvoiceId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public OfflineMethodConfig Method { get; set; } = null!;
    public string ResolvedReference { get; set; } = string.Empty;
    public string PendingPaymentId { get; set; } = string.Empty;
    public bool AlreadyMarkedSent { get; set; }
}
