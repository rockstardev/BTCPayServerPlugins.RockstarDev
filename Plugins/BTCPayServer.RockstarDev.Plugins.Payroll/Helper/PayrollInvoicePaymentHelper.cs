using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using System.Linq;
using static BTCPayServer.Models.WalletViewModels.ListTransactionsViewModel;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Helper
{
    public class PayrollInvoicePaymentHelper
    {
        public void AddPayrollTransaction(PayrollInvoice invoice, PayrollPluginDbContext context)
        {
            var existingTransaction = context.PayrollTransactions.SingleOrDefault(c => c.InvoiceId == invoice.Id);
            if (existingTransaction == null)
            {
                var entity = new PayrollTransaction
                {
                    Address = invoice.Destination,
                    UserId = invoice.UserId,
                    Recipient = invoice.User.Name,
                    InvoiceId = invoice.Id,
                    Currency = invoice.Currency,
                    Amount = invoice.Amount,
                    State = PayrollInvoiceState.AwaitingApproval
                };
                context.Add(entity);
            }
        }

        public void FinalizePayrollTransaction(PayrollInvoice invoice, TransactionViewModel transactionViewModel, PayrollPluginDbContext context)
        {
            var transaction = context.PayrollTransactions.SingleOrDefault(a => a.InvoiceId == invoice.Id);
            transaction.TransactionDate = transactionViewModel.Timestamp.UtcDateTime;
            transaction.State = PayrollInvoiceState.Completed;
            transaction.TransactionId = transactionViewModel.Id;
            transaction.Balance = transactionViewModel.Balance;
            transaction.Link = transactionViewModel.Link;
            context.Update(transaction);
        }
    }
}
