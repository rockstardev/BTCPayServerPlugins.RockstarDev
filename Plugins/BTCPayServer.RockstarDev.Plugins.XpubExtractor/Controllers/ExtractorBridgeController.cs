using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Blazor.VaultBridge;
using BTCPayServer.Blazor.VaultBridge.Elements;
using BTCPayServer.Hwi;
using BTCPayServer.RockstarDev.Plugins.XpubExtractor.Blazor;
using NBitcoin;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer.RockstarDev.Plugins.XpubExtractor.Controllers;

public class ExtractorBridgeController : HWIController
{
    private XPubSelectorVaultElement xpubSelect;

    protected override async Task Run(VaultBridgeUI ui, HwiClient hwi, HwiDeviceClient device, HDFingerprint fingerprint, BTCPayNetwork network,
        CancellationToken cancellationToken)
    {
        xpubSelect ??= new XPubSelectorVaultElement(ui, network.NBitcoinNetwork);
        var xpubInfo = await xpubSelect.GetXPubSelect();
        var scriptPubKeyTypeType = xpubInfo.ToScriptPubKeyType();
        ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Fetching public keys..."]);
        var keyPath = xpubInfo.ToKeyPath();
        if (!xpubInfo.IsCustom) keyPath = keyPath.Derive(network.CoinType).Derive(xpubInfo.AccountNumber, true);
        if (xpubInfo.IsMultiSig && scriptPubKeyTypeType != ScriptPubKeyType.Legacy)
        {
            var scriptPubIndex = scriptPubKeyTypeType switch
            {
                ScriptPubKeyType.Segwit => 2,
                ScriptPubKeyType.SegwitP2SH => 1,
                ScriptPubKeyType.TaprootBIP86 => 3,
                _ => throw new NotSupportedException("It should never happen")
            };
            keyPath = keyPath.Derive(scriptPubIndex, true);
        }

        var xpub = await device.GetXPubAsync(keyPath, cancellationToken);

        var suffix = scriptPubKeyTypeType switch
        {
            ScriptPubKeyType.Segwit => "",
            ScriptPubKeyType.Legacy => "-[legacy]",
            ScriptPubKeyType.SegwitP2SH => "-[p2sh]",
            ScriptPubKeyType.TaprootBIP86 => "-[taproot]",
            _ => throw new NotSupportedException($"Unsupported ScriptPubKeyType: {scriptPubKeyTypeType}")
        };
        var strategy = network.NBXplorerNetwork.DerivationStrategyFactory.Parse(xpub + suffix);
        ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["Public keys successfully fetched."]);

        ui.AddElement(new ShowXPubVaultElement(ui)
        {
            DerivationScheme = strategy.ToString(),
            KeyPath = keyPath.ToString(),
            RootFingerprint = fingerprint.ToString()
        });
        //ui.ShowRetry();
    }
}
