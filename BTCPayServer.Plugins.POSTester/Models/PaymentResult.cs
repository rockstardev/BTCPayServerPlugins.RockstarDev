namespace BTCPayServer.Plugins.POSTester.Models;

public class PaymentResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, double> TimingResults { get; set; } = new();
    public string? Invoice { get; set; }
    public string? PaymentId { get; set; }
    public double TotalTimeMs { get; set; }
}

public class PerformanceTimer : IDisposable
{
    private readonly string _operationName;
    private readonly DateTime _startTime;
    private readonly Action<string, double> _onComplete;

    public PerformanceTimer(string operationName, Action<string, double> onComplete)
    {
        _operationName = operationName;
        _onComplete = onComplete;
        _startTime = DateTime.UtcNow;
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Starting: {operationName}");
    }

    public void Dispose()
    {
        var elapsed = (DateTime.UtcNow - _startTime).TotalMilliseconds;
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Completed: {_operationName} in {elapsed:F2}ms");
        _onComplete(_operationName, elapsed);
    }
}
