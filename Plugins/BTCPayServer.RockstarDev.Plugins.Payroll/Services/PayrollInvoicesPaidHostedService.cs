using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Services;

public class PayrollInvoicesPaidHostedService : EventHostedServiceBase
{
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly PayrollPluginDbContextFactory _pluginDbContextFactory;

    public PayrollInvoicesPaidHostedService(PaymentMethodHandlerDictionary handlers,
        EventAggregator eventAggregator,
        PayrollPluginDbContextFactory pluginDbContextFactory,
        Logs logs) :
        base(eventAggregator, logs)
    {
        _handlers = handlers;
        _pluginDbContextFactory = pluginDbContextFactory;
    }

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
                    var network = _handlers.TryGetNetwork(transactionEvent.PaymentMethodId);
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
                        if (address is not null)
                        {
                            matchedObjects.Add(address.ToString());
                            amountPaid.Add(address.ToString(), txOut.TxOut.Value.ToString());
                        }
                    }

                    await using var dbPlugin = _pluginDbContextFactory.CreateContext();
                    var invoicesToBePaid = dbPlugin.PayrollInvoices
                        .Where(a => a.State == PayrollInvoiceState.AwaitingPayment || a.State == PayrollInvoiceState.InProgress)
                        .ToList();

                    foreach (var invoice in invoicesToBePaid)
                    {
                        if (matchedObjects.Contains(invoice.Destination))
                        {
                            invoice.TxnId = txHash;
                            invoice.State = PayrollInvoiceState.Completed;
                            invoice.BtcPaid = amountPaid[invoice.Destination];
                        }
                    }

                    await dbPlugin.SaveChangesAsync();

                    break;
                }
        }
    }
}
