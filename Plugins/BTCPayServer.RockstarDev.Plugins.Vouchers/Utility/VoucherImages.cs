using System.Collections.Generic;

namespace BTCPayServer.RockstarDev.Plugins.Vouchers.Utility;

public static class VoucherImages
{
    public static readonly Dictionary<string, string> ImageMap = new()
    {
        { "jack", "1000sats-jack.png" },
        { "odell", "1000sats-odell.png" },
        { "giacomo", "1000sats-giacomo.png" },
        { "luke", "1000sats-luke.png" }
    };

    public static string GetImageFile(string key) => ImageMap[key.ToLower()];
}
