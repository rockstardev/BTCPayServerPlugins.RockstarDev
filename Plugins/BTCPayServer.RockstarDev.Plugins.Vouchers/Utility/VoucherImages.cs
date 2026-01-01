using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BTCPayServer.RockstarDev.Plugins.Vouchers.Utility;

public static class VoucherImages
{
    public static readonly Dictionary<string, string> ImageMap = new()
    {
        { "jack", "1000sats-jack.png" },
        { "odell", "1000sats-odell.png" },
        { "giacomo", "1000sats-giacomo.png" },
        { "luke", "1000sats-luke.png" },
        { "nayib", "nayib_bukele.png" }
    };

    public static Stream GetImageStream(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"BTCPayServer.RockstarDev.Plugins.Vouchers.Resources.{fileName}";
        return assembly.GetManifestResourceStream(resourceName);
    }

    public static string GetImageFile(string key) => ImageMap[key.ToLower()];
}
