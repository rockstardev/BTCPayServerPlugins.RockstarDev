namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments;

public static class OfflinePaymentMethodType
{
    public const string ACH = "ACH";
    public const string Wire = "WIRE";

    public static readonly string[] KnownTypes = [ACH, Wire];
}
