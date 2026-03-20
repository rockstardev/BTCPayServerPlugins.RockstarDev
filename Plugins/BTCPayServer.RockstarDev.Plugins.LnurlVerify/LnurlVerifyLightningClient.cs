#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.RockstarDev.Plugins.LnurlVerify.Data;
using BTCPayServer.RockstarDev.Plugins.LnurlVerify.Data.Models;
using LNURL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Network = NBitcoin.Network;

namespace BTCPayServer.RockstarDev.Plugins.LnurlVerify;

/// <summary>
/// Lightning client that uses a Lightning Address with LUD-21 verify as a
/// receive-only payment source. No node, no API keys - just LNURL.
/// </summary>
public class LnurlVerifyLightningClient : IExtendedLightningClient
{
    private readonly string _lightningAddress;
    private readonly string _username;
    private readonly string _domain;
    private readonly Network _network;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly LnurlVerifyDbContextFactory _dbContextFactory;

    // In-memory cache, populated from DB on first access
    private readonly ConcurrentDictionary<string, InvoiceRecord> _invoices = new();
    private bool _cacheLoaded;

    private record InvoiceRecord(
        string PaymentHash,
        string Bolt11,
        string VerifyUrl,
        string InvoiceId,
        DateTimeOffset ExpiresAt,
        LightMoney Amount)
    {
        public bool IsExpired => ExpiresAt < DateTimeOffset.UtcNow;
    }

    public LnurlVerifyLightningClient(
        string lightningAddress,
        Network network,
        HttpClient httpClient,
        ILogger logger,
        LnurlVerifyDbContextFactory dbContextFactory)
    {
        if (string.IsNullOrWhiteSpace(lightningAddress))
            throw new ArgumentException("Lightning Address cannot be empty", nameof(lightningAddress));

        var parts = lightningAddress.Split('@');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            throw new ArgumentException(
                $"Invalid Lightning Address format: '{lightningAddress}'. Expected user@domain.com",
                nameof(lightningAddress));

        _lightningAddress = lightningAddress;
        _username = parts[0];
        _domain = parts[1];
        _network = network;
        _httpClient = httpClient;
        _logger = logger;
        _dbContextFactory = dbContextFactory;
    }

    public string? DisplayName => "LNURL Verify";
    public Uri? ServerUri => new Uri($"https://{_domain}");

    public override string ToString() => $"type=lnurlverify;address={_lightningAddress}";

    /// <summary>
    /// Loads unexpired invoices from the database into the in-memory cache.
    /// </summary>
    private async Task EnsureCacheLoaded()
    {
        if (_cacheLoaded) return;

        try
        {
            await using var db = _dbContextFactory.CreateContext();
            var now = DateTimeOffset.UtcNow;
            var dbInvoices = await db.Invoices
                .Where(i => i.ExpiresAt > now)
                .ToListAsync();

            // Bolt11 and Amount are not persisted - only available for invoices
            // created in the current process session
            foreach (var inv in dbInvoices)
            {
                var record = new InvoiceRecord(
                    inv.PaymentHash, string.Empty, inv.VerifyUrl, inv.InvoiceId,
                    inv.ExpiresAt, LightMoney.Zero);
                _invoices.TryAdd(inv.PaymentHash, record);
                _invoices.TryAdd(inv.InvoiceId, record);
            }

            if (dbInvoices.Count > 0)
                _logger.LogInformation("Loaded {Count} unexpired invoices from database", dbInvoices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load invoices from database, starting with empty cache");
        }

        _cacheLoaded = true;
    }

    /// <summary>
    /// Persists an invoice record to the database.
    /// </summary>
    private async Task PersistInvoice(InvoiceRecord record)
    {
        try
        {
            await using var db = _dbContextFactory.CreateContext();
            db.Invoices.Add(new LnurlVerifyInvoice
            {
                PaymentHash = record.PaymentHash,
                InvoiceId = record.InvoiceId,
                VerifyUrl = record.VerifyUrl,
                ExpiresAt = record.ExpiresAt
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist invoice {Hash} to database",
                record.PaymentHash[..12]);
        }
    }

    /// <summary>
    /// Resolves the Lightning Address and calls the LNURL-pay callback to get a BOLT11 invoice.
    /// </summary>
    public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description,
        TimeSpan expiry, CancellationToken cancellation = default)
    {
        return await CreateInvoice(new CreateInvoiceParams(amount, description, expiry),
            cancellation);
    }

    public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest,
        CancellationToken cancellation = default)
    {
        // Step 1: Resolve Lightning Address to LNURL-pay endpoint
        var lnurlpUrl = $"https://{_domain}/.well-known/lnurlp/{_username}";
        var payRequestJson = await _httpClient.GetStringAsync(lnurlpUrl, cancellation);
        var payRequest = JsonConvert.DeserializeObject<LNURLPayRequest>(payRequestJson);
        if (payRequest?.Callback == null)
            throw new InvalidOperationException("Failed to resolve Lightning Address LNURL-pay endpoint");

        // Step 2: Call callback with amount to get BOLT11
        var amountMsat = createInvoiceRequest.Amount.MilliSatoshi;
        var separator = payRequest.Callback.Query?.Length > 0 ? "&" : "?";
        var callbackUrl = $"{payRequest.Callback}{separator}amount={amountMsat}";

        var callbackJson = await _httpClient.GetStringAsync(callbackUrl, cancellation);
        var callbackResponse = JObject.Parse(callbackJson);

        var bolt11 = callbackResponse["pr"]?.Value<string>();
        if (string.IsNullOrEmpty(bolt11))
            throw new InvalidOperationException("LNURL-pay callback did not return a payment request");

        var verifyUrl = callbackResponse["verify"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(verifyUrl))
            throw new InvalidOperationException(
                "LNURL-pay callback did not return a LUD-21 verify URL. " +
                "The Lightning Address provider must support LUD-21 for payment verification.");

        // Step 3: Parse BOLT11 to extract payment hash
        var parsedBolt11 = BOLT11PaymentRequest.Parse(bolt11, _network);
        var paymentHash = parsedBolt11.PaymentHash?.ToString()
            ?? throw new InvalidOperationException("Could not extract payment hash from BOLT11");

        var invoiceId = paymentHash[..20]; // Use first 20 chars of hash as ID

        // Step 4: Cache and persist for later verification
        var expiry = createInvoiceRequest.Expiry == default
            ? TimeSpan.FromMinutes(10)
            : createInvoiceRequest.Expiry;
        var expiresAt = DateTimeOffset.UtcNow + expiry;

        var record = new InvoiceRecord(
            paymentHash, bolt11, verifyUrl, invoiceId,
            expiresAt, createInvoiceRequest.Amount);
        _invoices[paymentHash] = record;
        _invoices[invoiceId] = record;

        await PersistInvoice(record);

        _logger.LogInformation(
            "Created LNURL Verify invoice: hash={PaymentHash}",
            paymentHash[..12]);

        return new LightningInvoice
        {
            Id = invoiceId,
            PaymentHash = paymentHash,
            BOLT11 = bolt11,
            Amount = createInvoiceRequest.Amount,
            Status = LightningInvoiceStatus.Unpaid,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// Check invoice status via LUD-21 verify endpoint.
    /// </summary>
    public async Task<LightningInvoice?> GetInvoice(string invoiceId,
        CancellationToken cancellation = default)
    {
        await EnsureCacheLoaded();
        if (!_invoices.TryGetValue(invoiceId, out var record))
            return null;
        return await CheckInvoiceStatus(record, cancellation);
    }

    public async Task<LightningInvoice?> GetInvoice(uint256 paymentHash,
        CancellationToken cancellation = default)
    {
        await EnsureCacheLoaded();
        var hashStr = paymentHash.ToString();
        if (!_invoices.TryGetValue(hashStr, out var record))
            return null;
        return await CheckInvoiceStatus(record, cancellation);
    }

    private async Task<LightningInvoice> CheckInvoiceStatus(InvoiceRecord record,
        CancellationToken cancellation)
    {
        var status = LightningInvoiceStatus.Unpaid;
        string? preimage = null;

        try
        {
            var json = await _httpClient.GetStringAsync(record.VerifyUrl, cancellation);
            var verifyResponse = JObject.Parse(json);
            var settled = verifyResponse["settled"]?.Value<bool>() ?? false;
            if (settled)
            {
                status = LightningInvoiceStatus.Paid;
                preimage = verifyResponse["preimage"]?.Value<string>();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check verify URL for {Hash}",
                record.PaymentHash[..12]);
        }

        return new LightningInvoice
        {
            Id = record.InvoiceId,
            PaymentHash = record.PaymentHash,
            BOLT11 = record.Bolt11,
            Amount = record.Amount,
            Status = status,
            Preimage = preimage,
            ExpiresAt = record.ExpiresAt
        };
    }

    private void RemoveInvoice(InvoiceRecord record)
    {
        _invoices.TryRemove(record.PaymentHash, out _);
        _invoices.TryRemove(record.InvoiceId, out _);
    }

    /// <summary>
    /// Polls verify endpoints for settlement notifications.
    /// </summary>
    public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default)
    {
        await EnsureCacheLoaded();
        return new LnurlVerifyInvoiceListener(this, cancellation);
    }

    private class LnurlVerifyInvoiceListener : ILightningInvoiceListener
    {
        private readonly LnurlVerifyLightningClient _client;
        private readonly CancellationToken _cancellation;
        private readonly HashSet<string> _emittedHashes = new();

        public LnurlVerifyInvoiceListener(LnurlVerifyLightningClient client,
            CancellationToken cancellation)
        {
            _client = client;
            _cancellation = cancellation;
        }

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellation, cancellation);

            // Poll all pending invoices every 2 seconds
            while (!linked.Token.IsCancellationRequested)
            {
                // Deduplicate: iterate unique records by payment hash only
                var seen = new HashSet<string>();
                foreach (var kvp in _client._invoices)
                {
                    if (linked.Token.IsCancellationRequested) break;

                    var record = kvp.Value;
                    if (!seen.Add(record.PaymentHash)) continue;
                    if (_emittedHashes.Contains(record.PaymentHash)) continue;

                    // Skip expired invoices to avoid unnecessary verify URL requests
                    if (record.IsExpired)
                    {
                        _client.RemoveInvoice(record);
                        continue;
                    }

                    try
                    {
                        var invoice = await _client.CheckInvoiceStatus(record, linked.Token);
                        if (invoice.Status == LightningInvoiceStatus.Paid)
                        {
                            _emittedHashes.Add(record.PaymentHash);
                            _client.RemoveInvoice(record);
                            return invoice;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Continue polling other invoices
                    }
                }

                await Task.Delay(2000, linked.Token);
            }

            throw new OperationCanceledException();
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Returns basic info from the LNURL-pay metadata.
    /// </summary>
    public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default)
    {
        var info = new LightningNodeInformation();
        try
        {
            var lnurlpUrl = $"https://{_domain}/.well-known/lnurlp/{_username}";
            var json = await _httpClient.GetStringAsync(lnurlpUrl, cancellation);
            var payRequest = JsonConvert.DeserializeObject<LNURLPayRequest>(json);
            // LightningNodeInformation doesn't expose min/max sendable directly,
            // but we can log it for debugging
            if (payRequest != null)
            {
                _logger.LogInformation(
                    "LNURL Verify info: min={Min}msat, max={Max}msat",
                    payRequest.MinSendable?.MilliSatoshi,
                    payRequest.MaxSendable?.MilliSatoshi);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch LNURL-pay metadata");
        }
        return info;
    }

    /// <summary>
    /// Validates the Lightning Address resolves and supports LUD-21.
    /// </summary>
    public async Task<ValidationResult?> Validate()
    {
        try
        {
            var lnurlpUrl = $"https://{_domain}/.well-known/lnurlp/{_username}";
            using var response = await _httpClient.GetAsync(lnurlpUrl);
            if (!response.IsSuccessStatusCode)
            {
                return new ValidationResult(
                    $"Lightning Address {_lightningAddress} could not be resolved (HTTP {response.StatusCode})");
            }

            var json = await response.Content.ReadAsStringAsync();
            var payRequest = JsonConvert.DeserializeObject<LNURLPayRequest>(json);
            if (payRequest?.Callback == null)
            {
                return new ValidationResult(
                    $"Lightning Address {_lightningAddress} did not return a valid LNURL-pay response");
            }

            // Note: We can't verify LUD-21 support until an actual invoice is created
            // (the verify URL is in the callback response, not the initial metadata)
            return null; // Valid
        }
        catch (Exception ex)
        {
            return new ValidationResult(
                $"Failed to validate Lightning Address: {ex.Message}");
        }
    }

    // --- Unsupported operations (receive-only) ---

    public Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default)
        => Task.FromResult(Array.Empty<LightningInvoice>());

    public Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request,
        CancellationToken cancellation = default)
        => Task.FromResult(Array.Empty<LightningInvoice>());

    public Task<LightningPayment?> GetPayment(string paymentHash,
        CancellationToken cancellation = default)
        => throw new NotSupportedException("LNURL Verify is receive-only");

    public Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default)
        => throw new NotSupportedException("LNURL Verify is receive-only");

    public Task<LightningPayment[]> ListPayments(ListPaymentsParams request,
        CancellationToken cancellation = default)
        => throw new NotSupportedException("LNURL Verify is receive-only");

    public Task<PayResponse> Pay(PayInvoiceParams payParams,
        CancellationToken cancellation = default)
        => throw new NotSupportedException("LNURL Verify is receive-only");

    public Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams,
        CancellationToken cancellation = default)
        => throw new NotSupportedException("LNURL Verify is receive-only");

    public Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default)
        => throw new NotSupportedException("LNURL Verify is receive-only");

    public Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
        => Task.CompletedTask; // No-op: can't cancel external invoices

    public Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = default)
        => throw new NotSupportedException("LNURL Verify does not expose balance");

    public Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
        => throw new NotSupportedException("LNURL Verify does not support on-chain deposits");

    public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest,
        CancellationToken cancellation = default)
        => throw new NotSupportedException("LNURL Verify does not support channel management");

    public Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo,
        CancellationToken cancellation = default)
        => throw new NotSupportedException("LNURL Verify does not support peer connections");

    public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default)
        => Task.FromResult(Array.Empty<LightningChannel>());
}
