using System.IO;
using System.Linq;
using System.Reflection;

namespace BTCPayServer.RockstarDev.Plugins.TransactionCounter.ViewModels;

public static class HtmlTemplates
{
    private static string _default;
    public static string Default
    {
        get
        {
            if (string.IsNullOrEmpty(_default))
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith("HistoricalTemplates.default.html"));
                using var stream = assembly.GetManifestResourceStream(resourceName);
                using var reader = new StreamReader(stream);
                _default = reader.ReadToEnd();
            }
            return _default;
        }
    }
}
