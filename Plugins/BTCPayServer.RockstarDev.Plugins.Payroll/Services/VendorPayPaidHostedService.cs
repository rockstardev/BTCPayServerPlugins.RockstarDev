using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BTCPayServer.RockstarDev.Plugins.Payroll.Services.EmailService;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Services;

public class VendorPayPaidHostedService(
    EmailService emailService,
    StoreRepository _storeRepo,
    EventAggregator eventAggregator,
    PaymentMethodHandlerDictionary handlers,
    PayrollPluginDbContextFactory pluginDbContextFactory,
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

                    var amountPaid = new Dictionary<string,string>();
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
                        amountPaid.Add(address.ToString(), txOut.TxOut.Value.ToString());
                    }

                    await using var dbPlugin = pluginDbContextFactory.CreateContext();

                    var invoicesToBePaid = dbPlugin.PayrollInvoices
                        .Where(a => (a.State == PayrollInvoiceState.AwaitingPayment || a.State == PayrollInvoiceState.InProgress)
                                    && matchedObjects.Contains(a.Destination))
                        .Include(c => c.User)
                        .ToList();

                    foreach (var invoice in invoicesToBePaid)
                    {
                        invoice.TxnId = txHash;
                        invoice.State = PayrollInvoiceState.Completed;
                        invoice.BtcPaid = amountPaid[invoice.Destination];
                        invoice.PaidAt = DateTimeOffset.UtcNow;
                    }

                    await dbPlugin.SaveChangesAsync(cancellationToken);
                    await SendSuccessfulInvoicePaymentEmail(invoicesToBePaid.Where(c => c.State == PayrollInvoiceState.Completed).ToList());
                    break;
                }
        }
    }

    private async Task SendSuccessfulInvoicePaymentEmail(List<PayrollInvoice> invoices)
    {
        if (!invoices.Any())
            return;

        var invoicesByStore = invoices.GroupBy(i => i.User.StoreId);
        var emailRecipients = new List<EmailRecipient>();
        const string subject = "Invoice payment completed successfully";

        foreach (var storeGroup in invoicesByStore)
        {
            var setting = await pluginDbContextFactory.GetSettingAsync(storeGroup.Key);
            if (setting?.EmailVendorOnInvoicePaid != true)
                continue;

            foreach (var invoice in storeGroup)
            {
                var storeName = (await _storeRepo.FindStore(invoice.User.StoreId))?.StoreName;
                emailRecipients.Add(new EmailRecipient
                {
                    Address = InternetAddress.Parse(invoice.User.Email),
                    Subject = subject,
                    MessageText = setting.EmailTemplate
                        .Replace("{Name}", invoice.User.Name)
                        .Replace("{StoreName}", storeName)
                        .Replace("{CreatedDate}", invoice.CreatedAt.ToString("D"))
                        .Replace("{DatePaid}", invoice.PaidAt?.ToString("D"))
                        .Replace("{VendorPayLink}", $"{setting.VendorPayPublicLink}")
                });
            }
        }

        if (emailRecipients.Any())
        {
            await emailService.SendBulkEmail(emailRecipients);
        }
    }
}
