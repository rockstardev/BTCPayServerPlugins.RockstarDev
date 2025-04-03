using System.Net.Http;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.Payroll;

public static class UtilExt
{
    public static async Task<byte[]> DownloadFileAsByteArray(this HttpClient httpClient, string fileUrl)
    {
        using var response = await httpClient.GetAsync(fileUrl);
        return await response.Content.ReadAsByteArrayAsync();
    }
}
