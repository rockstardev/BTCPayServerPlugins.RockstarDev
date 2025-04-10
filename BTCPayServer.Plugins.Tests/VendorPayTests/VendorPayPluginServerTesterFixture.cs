using BTCPayServer.Tests;

namespace BTCPayServer.Plugins.Tests.VendorPayTests;

public class VendorPayPluginServerTesterFixture : IDisposable
{
    public ServerTester ServerTester { get; private set; }

    public void Initialize(PlaywrightBaseTest testInstance)
    {
        if (ServerTester == null)
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "VendorPayPluginUITest");
            ServerTester = testInstance.CreateServerTester(testDir, newDb: true);
            ServerTester.StartAsync().GetAwaiter().GetResult();
        }
    }
    public void Dispose()
    {
        ServerTester?.Dispose();
        ServerTester = null;
    }
}
