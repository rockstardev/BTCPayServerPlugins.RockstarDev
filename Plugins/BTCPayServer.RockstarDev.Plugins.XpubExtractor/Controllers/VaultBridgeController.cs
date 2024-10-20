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
public class VaultBridgeController : Controller
{
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly IAuthorizationService _authorizationService;

    public VaultBridgeController(PaymentMethodHandlerDictionary handlers, IAuthorizationService authorizationService)
    {
        _handlers = handlers;
        _authorizationService = authorizationService;
    }
    
    // This is a websocket endpoint that is used by javascript to fetch information from a hardware wallet
    [Route("~/plugins/xpubextractor/vaultbridgeconnection")]
    public async Task<IActionResult> VaultBridgeConnection(string cryptoCode = null)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
            return NotFound();
        
        WalletId walletId = new WalletId(CurrentStore.Id, "BTC");
        cryptoCode = cryptoCode ?? walletId.CryptoCode;
        bool versionChecked = false;
        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
        {
            var cancellationToken = cts.Token;
            if (!_handlers.TryGetValue(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode), out var h) ||
                h is not IHasNetwork { Network: var network })
                return NotFound();
            var websocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var vaultClient = new VaultClient(websocket);
            var hwi = new Hwi.HwiClient(network.NBitcoinNetwork) { Transport = new VaultHWITransport(vaultClient) };
            Hwi.HwiDeviceClient device = null;
            HwiEnumerateEntry deviceEntry = null;
            HDFingerprint? fingerprint = null;
            string password = null;
            var websocketHelper = new WebSocketHelper(websocket);

            async Task FetchFingerprint()
            {
                fingerprint = (await device.GetXPubAsync(new KeyPath("44'"), cancellationToken)).ExtPubKey
                    .ParentFingerprint;
                device = new HwiDeviceClient(hwi, DeviceSelectors.FromFingerprint(fingerprint.Value), deviceEntry.Model,
                    fingerprint) { Password = password };
            }

            async Task<bool> RequireDeviceUnlocking()
            {
                if (deviceEntry == null)
                {
                    await websocketHelper.Send("{ \"error\": \"need-device\"}", cancellationToken);
                    return true;
                }

                if (deviceEntry.Code is HwiErrorCode.DeviceNotInitialized)
                {
                    await websocketHelper.Send("{ \"error\": \"need-initialized\"}", cancellationToken);
                    return true;
                }

                if (deviceEntry.Code is HwiErrorCode.DeviceNotReady)
                {
                    if (IsTrezorT(deviceEntry))
                    {
                        await websocketHelper.Send("{ \"error\": \"need-passphrase-on-device\"}", cancellationToken);
                        return true;
                    }
                    else if (deviceEntry.NeedsPinSent is true)
                    {
                        await websocketHelper.Send("{ \"error\": \"need-pin\"}", cancellationToken);
                        return true;
                    }
                    else if (deviceEntry.NeedsPassphraseSent is true && password is null)
                    {
                        await websocketHelper.Send("{ \"error\": \"need-passphrase\"}", cancellationToken);
                        return true;
                    }
                }

                if (IsTrezorOne(deviceEntry) && password is null)
                {
                    fingerprint = null; // There will be a new fingerprint
                    device = new HwiDeviceClient(hwi, DeviceSelectors.FromDeviceType("trezor", deviceEntry.Path),
                        deviceEntry.Model, null);
                    await websocketHelper.Send("{ \"error\": \"need-passphrase\"}", cancellationToken);
                    return true;
                }

                return false;
            }

            bool IsTrezorT(HwiEnumerateEntry deviceEntry)
            {
                return deviceEntry.Model.Contains("Trezor_T", StringComparison.OrdinalIgnoreCase);
            }

            bool IsTrezorOne(HwiEnumerateEntry deviceEntry)
            {
                return deviceEntry.Model.Contains("trezor_1", StringComparison.OrdinalIgnoreCase);
            }

            JObject o = null;
            try
            {
                while (true)
                {
                    var command = await websocketHelper.NextMessageAsync(cancellationToken);
                    switch (command)
                    {
                        case "set-passphrase":
                            device.Password = await websocketHelper.NextMessageAsync(cancellationToken);
                            password = device.Password;
                            break;
                        case "ask-sign":
                            if (await RequireDeviceUnlocking())
                            {
                                continue;
                            }

                            if (walletId == null)
                            {
                                await websocketHelper.Send("{ \"error\": \"invalid-walletId\"}", cancellationToken);
                                continue;
                            }

                            if (fingerprint is null)
                            {
                                await FetchFingerprint();
                            }

                            await websocketHelper.Send("{ \"info\": \"ready\"}", cancellationToken);
                            o = JObject.Parse(await websocketHelper.NextMessageAsync(cancellationToken));
                            var authorization =
                                await _authorizationService.AuthorizeAsync(User, Policies.CanModifyStoreSettings);
                            if (!authorization.Succeeded)
                            {
                                await websocketHelper.Send("{ \"error\": \"not-authorized\"}", cancellationToken);
                                continue;
                            }

                            var psbt = PSBT.Parse(o["psbt"].Value<string>(), network.NBitcoinNetwork);
                            var derivationSettings = GetDerivationSchemeSettings(walletId);
                            derivationSettings.RebaseKeyPaths(psbt);
                            var signing = derivationSettings.GetSigningAccountKeySettings();
                            if (signing.GetRootedKeyPath()?.MasterFingerprint != fingerprint)
                            {
                                await websocketHelper.Send("{ \"error\": \"wrong-wallet\"}", cancellationToken);
                                continue;
                            }

                            var signableInputs = psbt.Inputs.SelectMany(i => i.HDKeyPaths)
                                .Where(i => i.Value.MasterFingerprint == fingerprint).ToArray();
                            if (signableInputs.Length > 0)
                            {
                                var actualPubKey = (await device.GetXPubAsync(signableInputs[0].Value.KeyPath))
                                    .GetPublicKey();
                                if (actualPubKey != signableInputs[0].Key)
                                {
                                    await websocketHelper.Send("{ \"error\": \"wrong-keypath\"}", cancellationToken);
                                    continue;
                                }
                            }

                            try
                            {
                                psbt = await device.SignPSBTAsync(psbt, cancellationToken);
                            }
                            catch (Hwi.HwiException)
                            {
                                await websocketHelper.Send("{ \"error\": \"user-reject\"}", cancellationToken);
                                continue;
                            }

                            o = new JObject();
                            o.Add("psbt", psbt.ToBase64());
                            await websocketHelper.Send(o.ToString(), cancellationToken);
                            break;
                        case "display-address":
                            if (await RequireDeviceUnlocking())
                            {
                                continue;
                            }

                            var k = RootedKeyPath.Parse(await websocketHelper.NextMessageAsync(cancellationToken));
                            await device.DisplayAddressAsync(GetScriptPubKeyType(k), k.KeyPath, cancellationToken);
                            await websocketHelper.Send("{ \"info\": \"ok\"}", cancellationToken);
                            break;
                        case "ask-pin":
                            if (device == null)
                            {
                                await websocketHelper.Send("{ \"error\": \"need-device\"}", cancellationToken);
                                continue;
                            }

                            try
                            {
                                await device.PromptPinAsync(cancellationToken);
                            }
                            catch (HwiException ex) when (ex.ErrorCode == HwiErrorCode.DeviceAlreadyUnlocked)
                            {
                                await websocketHelper.Send("{ \"error\": \"device-already-unlocked\"}",
                                    cancellationToken);
                                continue;
                            }

                            await websocketHelper.Send("{ \"info\": \"prompted, please input the pin\"}",
                                cancellationToken);
                            var pin = int.Parse(await websocketHelper.NextMessageAsync(cancellationToken),
                                CultureInfo.InvariantCulture);
                            if (await device.SendPinAsync(pin, cancellationToken))
                            {
                                goto askdevice;
                            }
                            else
                            {
                                await websocketHelper.Send("{ \"error\": \"incorrect-pin\"}", cancellationToken);
                                continue;
                            }
                        case "ask-xpub":
                            if (await RequireDeviceUnlocking()) continue;
                            await websocketHelper.Send("{ \"info\": \"ok\"}", cancellationToken);
                            var askedXpub = JObject.Parse(await websocketHelper.NextMessageAsync(cancellationToken));
                            var signatureType = askedXpub["signatureType"].Value<string>();
                            var addressType = askedXpub["addressType"].Value<string>();
                            var accountNumber = askedXpub["accountNumber"].Value<int>();
                            if (fingerprint is null) await FetchFingerprint();
                            var keyPath = GetKeyPath(signatureType, addressType, network.CoinType, accountNumber);
                            if (keyPath is null)
                            {
                                await websocketHelper.Send("{ \"error\": \"invalid-addresstype\"}", cancellationToken);
                                continue;
                            }

                            var xpub = await device.GetXPubAsync(keyPath);
                            if (!network.NBitcoinNetwork.Consensus.SupportSegwit && addressType != "legacy")
                            {
                                await websocketHelper.Send("{ \"error\": \"segwit-notsupported\"}", cancellationToken);
                                continue;
                            }

                            if (!network.NBitcoinNetwork.Consensus.SupportTaproot && addressType == "taproot")
                            {
                                await websocketHelper.Send("{ \"error\": \"taproot-notsupported\"}", cancellationToken);
                                continue;
                            }

                            // Create derivation strategy based on address type and signature type
                            var factory = network.NBXplorerNetwork.DerivationStrategyFactory;
                            var strategy = CreateDerivationStrategy(factory, xpub, addressType, signatureType);
                            if (strategy is null)
                            {
                                await websocketHelper.Send("{ \"error\": \"unsupported-signature-type\"}",
                                    cancellationToken);
                                continue;
                            }

                            // Create result JSON object with relevant information
                            JObject result = new JObject
                            {
                                ["fingerprint"] = fingerprint.Value.ToString(),
                                ["strategy"] = strategy.ToString(),
                                ["accountKey"] = xpub.ToString(),
                                ["keyPath"] = keyPath.ToString()
                            };
                            await websocketHelper.Send(result.ToString(), cancellationToken);

                            // Helper methods
                            KeyPath GetKeyPath(string signatureType, string addressType, KeyPath coinType,
                                int accountNumber)
                            {
                                return (signatureType, addressType) switch
                                {
                                    ("singlesig", "taproot") => new KeyPath("86'").Derive(coinType)
                                        .Derive(accountNumber, true),
                                    ("singlesig", "segwit") => new KeyPath("84'").Derive(coinType)
                                        .Derive(accountNumber, true),
                                    ("singlesig", "segwitWrapped") => new KeyPath("49'").Derive(coinType)
                                        .Derive(accountNumber, true),
                                    ("singlesig", "legacy") => new KeyPath("44'").Derive(coinType)
                                        .Derive(accountNumber, true),
                                    ("multisig", "segwit") => new KeyPath("48'").Derive(coinType)
                                        .Derive(accountNumber, true).Derive(2, true),
                                    ("multisig", "segwitWrapped") => new KeyPath("48'").Derive(coinType)
                                        .Derive(accountNumber, true).Derive(1, true),
                                    ("multisig", "legacy") => new KeyPath("45'").Derive(coinType)
                                        .Derive(accountNumber, true),
                                    ("multisig", "taproot") => new KeyPath("48'").Derive(coinType)
                                        .Derive(accountNumber, true).Derive(3, true),
                                    _ => null
                                };
                            }

                            DerivationStrategyBase CreateDerivationStrategy(DerivationStrategyFactory factory,
                                BitcoinExtPubKey xpub, string addressType, string signatureType)
                            {
                                // if (signatureType == "multisig")
                                // {
                                //     return factory.CreateMultiSigDerivationStrategy(new[] { xpub }, 2, new DerivationStrategyOptions
                                //     {
                                //         ScriptPubKeyType = addressType switch
                                //         {
                                //             "segwit" => ScriptPubKeyType.Segwit,
                                //             "segwitWrapped" => ScriptPubKeyType.SegwitP2SH,
                                //             "legacy" => ScriptPubKeyType.Legacy,
                                //             "taproot" => ScriptPubKeyType.TaprootBIP86,
                                //             _ => ScriptPubKeyType.Legacy
                                //         }
                                //     });
                                // }
                                return addressType switch
                                {
                                    "taproot" => factory.CreateDirectDerivationStrategy(xpub,
                                        new DerivationStrategyOptions
                                        {
                                            ScriptPubKeyType = ScriptPubKeyType.TaprootBIP86
                                        }),
                                    "segwit" => factory.CreateDirectDerivationStrategy(xpub,
                                        new DerivationStrategyOptions { ScriptPubKeyType = ScriptPubKeyType.Segwit }),
                                    "segwitWrapped" => factory.CreateDirectDerivationStrategy(xpub,
                                        new DerivationStrategyOptions
                                        {
                                            ScriptPubKeyType = ScriptPubKeyType.SegwitP2SH
                                        }),
                                    "legacy" => factory.CreateDirectDerivationStrategy(xpub,
                                        new DerivationStrategyOptions { ScriptPubKeyType = ScriptPubKeyType.Legacy }),
                                    _ => null
                                };
                            }
                            // End of ask-xpub

                            break;
                        case "ask-passphrase":
                            if (command == "ask-passphrase")
                            {
                                if (deviceEntry == null)
                                {
                                    await websocketHelper.Send("{ \"error\": \"need-device\"}", cancellationToken);
                                    continue;
                                }

                                // The make the trezor T ask for password
                                await device.GetXPubAsync(new KeyPath("44'"), cancellationToken);
                            }

                            goto askdevice;
                        case "ask-device":
                            askdevice:
                            if (!versionChecked)
                            {
                                var version = await hwi.GetVersionAsync(cancellationToken);
                                if (version.Major < 2)
                                {
                                    await websocketHelper.Send("{ \"error\": \"vault-outdated\"}", cancellationToken);
                                    continue;
                                }

                                versionChecked = true;
                            }

                            password = null;
                            deviceEntry = null;
                            device = null;
                            var entries = (await hwi.EnumerateEntriesAsync(cancellationToken)).ToList();
                            deviceEntry = entries.FirstOrDefault();
                            if (deviceEntry == null)
                            {
                                await websocketHelper.Send("{ \"error\": \"no-device\"}", cancellationToken);
                                continue;
                            }

                            var model = deviceEntry.Model ??
                                        "Unsupported hardware wallet, try to update BTCPay Server Vault";
                            device = new HwiDeviceClient(hwi, deviceEntry.DeviceSelector, model,
                                deviceEntry.Fingerprint);
                            fingerprint = device.Fingerprint;
                            JObject json = new JObject();
                            json.Add("model", model);
                            await websocketHelper.Send(json.ToString(), cancellationToken);
                            break;
                    }
                }
            }
            catch (FormatException ex)
            {
                JObject obj = new JObject();
                obj.Add("error", "invalid-network");
                obj.Add("details", ex.ToString());
                try
                {
                    await websocketHelper.Send(obj.ToString(), cancellationToken);
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                JObject obj = new JObject();
                obj.Add("error", "unknown-error");
                obj.Add("message", ex.Message);
                obj.Add("details", ex.ToString());
                try
                {
                    await websocketHelper.Send(obj.ToString(), cancellationToken);
                }
                catch
                {
                }
            }
            finally
            {
                await websocketHelper.DisposeAsync(cancellationToken);
            }
        }

        return new EmptyResult();
    }

    private ScriptPubKeyType GetScriptPubKeyType(RootedKeyPath keyPath)
    {
        var path = keyPath.KeyPath.ToString();
        if (path.StartsWith("86'", StringComparison.OrdinalIgnoreCase)) return ScriptPubKeyType.TaprootBIP86;
        if (path.StartsWith("84'", StringComparison.OrdinalIgnoreCase)) return ScriptPubKeyType.Segwit;
        if (path.StartsWith("49'", StringComparison.OrdinalIgnoreCase)) return ScriptPubKeyType.SegwitP2SH;
        if (path.StartsWith("44'", StringComparison.OrdinalIgnoreCase)) return ScriptPubKeyType.Legacy;
        throw new NotSupportedException("Unsupported keypath");
    }

    private DerivationSchemeSettings GetDerivationSchemeSettings(WalletId walletId)
    {
        var pmi = Payments.PaymentTypes.CHAIN.GetPaymentMethodId(walletId.CryptoCode);
        return CurrentStore.GetPaymentMethodConfig<DerivationSchemeSettings>(pmi, _handlers);
    }

    public StoreData CurrentStore
    {
        get { return HttpContext.GetStoreData(); }
    }

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