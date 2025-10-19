namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Services;

public class SweepResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public string TxId { get; set; }
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public SweepResultType ResultType { get; set; }

    public enum SweepResultType
    {
        Success,
        Failed,
        InsufficientBalance,
        BelowMinimumThreshold,
        NoConfiguration
    }

    public static SweepResult SuccessResult(string txId, decimal amount, decimal fee)
    {
        return new SweepResult
        {
            Success = true,
            TxId = txId,
            Amount = amount,
            Fee = fee,
            ResultType = SweepResultType.Success
        };
    }

    public static SweepResult FailureResult(string errorMessage, SweepResultType resultType = SweepResultType.Failed)
    {
        return new SweepResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            ResultType = resultType
        };
    }
}
