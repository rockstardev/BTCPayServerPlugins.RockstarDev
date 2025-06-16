using BTCPayServer.Tests;
using System;
using System.IO;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

[CollectionDefinition("Plugin Tests")]
public class PluginTestCollection : ICollectionFixture<SharedPluginTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

public class SharedPluginTestFixture : IDisposable
{
    public ServerTester ServerTester { get; private set; }

    public void Dispose()
    {
        ServerTester?.Dispose();
        ServerTester = null;
    }

    public void Initialize(PlaywrightBaseTest testInstance)
    {
        if (ServerTester == null)
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "SharedPluginTests");
            ServerTester = testInstance.CreateServerTester(testDir, true);
            ServerTester.StartAsync().GetAwaiter().GetResult();
        }
    }
}
