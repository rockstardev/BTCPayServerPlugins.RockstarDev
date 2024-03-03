namespace BTCPayServer.RockstarDev.Plugins.Payroll
{
    public enum PayrollInvoiceState
    {
        AwaitingApproval,
        AwaitingPayment,
        InProgress, // waiting for confirmation on blockchain (or for lightning it can be stuck HTLC
        Completed,
        Cancelled
    }
}
