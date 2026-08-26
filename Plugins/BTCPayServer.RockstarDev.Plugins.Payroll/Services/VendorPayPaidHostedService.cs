using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Services;

public class VendorPayPaidHostedService(
    EmailService emailService,
    EventAggregator eventAggregator,
    PaymentMethodHandlerDictionary handlers,
    PluginDbContextFactory pluginDbContextFactory,
    Logs logs)
    : EventHostedServiceBase(eventAggregator, logs)
{
    protected override void SubscribeToEvents()
    {
        Subscribe<NewOnChainTransactionEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        switch (evt)
        {
            // For each new transaction that we detect, we check if we can find
            // any utxo or script object matching it.
            // If we find, then we create a link between them and the tx object.
            case NewOnChainTransactionEvent transactionEvent:
            {
                var network = handlers.TryGetNetwork(transactionEvent.PaymentMethodId);
                var derivation = transactionEvent.NewTransactionEvent.DerivationStrategy;
                if (network is null || derivation is null)
                    break;
                var txHash = transactionEvent.NewTransactionEvent.TransactionData.TransactionHash.ToString();

                // find all wallet objects that fit this transaction
                // that means see if there are any utxo objects that match in/outs and scripts/addresses that match outs

                var matchedObjects = new List<string>();

                var amountSats = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                // Check if outputs match some UTXOs
                var walletOutputsByIndex = transactionEvent.NewTransactionEvent.Outputs.ToDictionary(o => (uint)o.Index);
                foreach (var txOut in transactionEvent.NewTransactionEvent.TransactionData.Transaction.Outputs.AsIndexedOutputs())
                {
                    BitcoinAddress address = null;

                    if (walletOutputsByIndex.TryGetValue(txOut.N, out var walletTxOut))
                        address = walletTxOut.Address;
                    address ??= txOut.TxOut.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork);

                    if (address is null)
                        continue;

                    matchedObjects.Add(address.ToString());
                    amountSats[address.ToString()] = txOut.TxOut.Value.Satoshi;
                }

                await using var dbPlugin = pluginDbContextFactory.CreateContext();

                var pendingStates = new[] { VendorPayInvoiceState.AwaitingPayment, VendorPayInvoiceState.InProgress };
                var directMatches = dbPlugin.PayrollInvoices
                    .Where(a => pendingStates.Contains(a.State) && matchedObjects.Contains(a.Destination))
                    .Include(c => c.User)
                    .ToList();

                // Stonewall split invoices can be paid entirely through their extra
                // addresses, so also scan pending split invoices for an intersection
                // with the observed outputs.
                var splitCandidates = dbPlugin.PayrollInvoices
                    .Where(a => pendingStates.Contains(a.State) && a.ExtraAddresses != null && a.ExtraAddresses != "")
                    .Include(c => c.User)
                    .ToList();
                var matched = new HashSet<string>(matchedObjects, StringComparer.OrdinalIgnoreCase);
                var invoicesToBePaid = directMatches
                    .Concat(splitCandidates.Where(a => directMatches.All(d => d.Id != a.Id)
                                                       && InvoiceAddresses(a).Any(matched.Contains)))
                    .ToList();

                var completing = SelectInvoicesToComplete(invoicesToBePaid, amountSats);
                foreach (var invoice in completing)
                {
                    invoice.TxnId = txHash;
                    invoice.State = VendorPayInvoiceState.Completed;
                    var paidSats = InvoiceAddresses(invoice).Sum(a => amountSats.GetValueOrDefault(a));
                    invoice.BtcPaid = new Money(paidSats, MoneyUnit.Satoshi).ToString();
                    invoice.PaidAt = DateTimeOffset.UtcNow;
                }

                await dbPlugin.SaveChangesAsync(cancellationToken);
                await emailService.SendSuccessfulInvoicePaymentEmail(invoicesToBePaid.Where(c => c.State == VendorPayInvoiceState.Completed).ToList());
                break;
            }
        }
    }

    // Decide which pending invoices are covered by the observed on-chain output
    // amounts. Iterates oldest-first and consumes a per-address budget so a
    // single output cannot satisfy multiple invoices sharing the same address.
    // A Stonewall split invoice completes when the SUM of observed amounts
    // across its destination plus extra addresses reaches the expected total;
    // the consumed budget is drawn from all of those addresses. Skips any
    // invoice with a null expected amount so legacy in-flight rows do not
    // complete on address-match alone (fail-closed on missing expected).
    public static List<PayrollInvoice> SelectInvoicesToComplete(
        IReadOnlyCollection<PayrollInvoice> pending,
        IReadOnlyDictionary<string, long> observedSatsByDestination)
    {
        var completing = new List<PayrollInvoice>();
        if (pending == null || pending.Count == 0)
            return completing;
        var budget = observedSatsByDestination == null
            ? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, long>(observedSatsByDestination, StringComparer.OrdinalIgnoreCase);
        foreach (var invoice in pending.OrderBy(i => i.CreatedAt))
        {
            if (!invoice.AmountSats.HasValue)
                continue;
            var addresses = InvoiceAddresses(invoice);
            if (addresses.Sum(a => budget.GetValueOrDefault(a)) < invoice.AmountSats.Value)
                continue;
            var remaining = invoice.AmountSats.Value;
            foreach (var address in addresses)
            {
                if (remaining <= 0)
                    break;
                if (!budget.TryGetValue(address, out var availableAt) || availableAt <= 0)
                    continue;
                var take = Math.Min(availableAt, remaining);
                budget[address] = availableAt - take;
                remaining -= take;
            }
            completing.Add(invoice);
        }
        return completing;
    }

    public static string[] InvoiceAddresses(PayrollInvoice invoice)
    {
        if (string.IsNullOrWhiteSpace(invoice.ExtraAddresses))
            return new[] { invoice.Destination };
        return new[] { invoice.Destination }
            .Concat(StonewallSplitter.SplitStoredExtras(invoice.ExtraAddresses))
            .ToArray();
    }
}
