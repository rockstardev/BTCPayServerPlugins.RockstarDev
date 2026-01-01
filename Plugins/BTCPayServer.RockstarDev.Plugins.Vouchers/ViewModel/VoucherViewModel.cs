using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payouts;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.RockstarDev.Plugins.Vouchers.ViewModel;


public class VoucherViewModel
{
    public string Id { get; set; }
    public string Name { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
    public PayoutMethodId[] PayoutMethods { get; set; }
    public PullPaymentsModel.PullPaymentModel.ProgressModel Progress { get; set; }
    public string StoreName { get; set; }
    public string LogoUrl { get; set; }
    public string BrandColor { get; set; }
    public string CssUrl { get; set; }
    public bool SupportsLNURL { get; set; }
    public string Description { get; set; }
    public string VoucherImage { get; set; }

    public async Task<VoucherViewModel> SetStoreBranding(HttpRequest request, UriResolver uriResolver, StoreBlob storeBlob)
    {
        var branding = await StoreBrandingViewModel.CreateAsync(request, uriResolver, storeBlob);
        LogoUrl = branding.LogoUrl;
        CssUrl = branding.CssUrl;
        BrandColor = branding.BrandColor;
        return this;
    }
}

public class ListVoucherViewModel
{
    public string? SearchText { get; set; }
    public VoucherPaymentState ActiveState { get; set; } = VoucherPaymentState.Active;
    public List<VoucherViewModel> Vouchers { get; set; }
}

public enum VoucherPaymentState
{
    Active,
    //Expired,
    Archived
}
