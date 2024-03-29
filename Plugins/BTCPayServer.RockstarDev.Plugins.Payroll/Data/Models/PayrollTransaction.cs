using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models
{
    public class PayrollTransaction
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }
        public string UserId { get; set; }
        public DateTime TransactionDate { get; set; }
        public string Recipient { get; set; }
        public string InvoiceId { get; set; }
        public string Link { get; set; }
        public string Address { get; set; }
        public string Currency { get; set; }
        public decimal Amount { get; set; }
        public decimal BtcUsdRate { get; set; }
        public decimal BtcJpyRate { get; set; }
        public decimal BtcAmount { get; set; }
        public string Balance { get; set; }
        public decimal MinerFee { get; set; }
        public string TransactionId { get; set; }
        public PayrollInvoiceState State { get; set; }
    }
}
