using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Hwi;
using BTCPayServer.ModelBinders;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.RockstarDev.Plugins.XpubExtractor.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class XpubExtractorController : Controller
{
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly IAuthorizationService _authorizationService;

    public XpubExtractorController(PaymentMethodHandlerDictionary handlers, IAuthorizationService authorizationService)
    {
        _handlers = handlers;
        _authorizationService = authorizationService;
    }

    [HttpGet("~/plugins/xpubextractor/")]
    public IActionResult Index()
    {
        return View(new IndexViewModel
        {
            CryptoCode = "BTC"
        });
    }

    // view model
    public class IndexViewModel
    {
        [Display(Name = "Derivation scheme")] public string DerivationScheme { get; set; }
        public string CryptoCode { get; set; }
        public string KeyPath { get; set; }
        [Display(Name = "Root fingerprint")] public string RootFingerprint { get; set; }
        public bool Confirmation { get; set; }
        [Display(Name = "Wallet file")] public IFormFile WalletFile { get; set; }

        [Display(Name = "Wallet file content")]
        public string WalletFileContent { get; set; }

        public string Config { get; set; }
        public string Source { get; set; }

        [Display(Name = "Derivation scheme format")]
        public string DerivationSchemeFormat { get; set; }

        [Display(Name = "Account key")] public string AccountKey { get; set; }
        public BTCPayNetwork Network { get; set; }
        [Display(Name = "Can use hot wallet")] public bool CanUseHotWallet { get; set; }
        [Display(Name = "Can use RPC import")] public bool CanUseRPCImport { get; set; }
        public bool SupportSegwit { get; set; }
        public bool SupportTaproot { get; set; }

        public RootedKeyPath GetAccountKeypath()
        {
            if (KeyPath != null && RootFingerprint != null && NBitcoin.KeyPath.TryParse(KeyPath, out var p) &&
                HDFingerprint.TryParse(RootFingerprint, out var fp))
            {
                return new RootedKeyPath(fp, p);
            }

            return null;
        }
    }
}