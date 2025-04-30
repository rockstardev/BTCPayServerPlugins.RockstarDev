using System.Reflection;
using System.Text.Json;

var currentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

var repoRoot = Path.GetFullPath(Path.Combine(currentDirectory, ".."));
while (!Directory.Exists(Path.Combine(repoRoot, ".git")) &&
       !Directory.Exists(Path.Combine(repoRoot, ".github")) &&
       Directory.GetParent(repoRoot) != null)
    repoRoot = Directory.GetParent(repoRoot).FullName;
var pluginsDirectory = Path.Combine(repoRoot, "Plugins");
if (!Directory.Exists(pluginsDirectory))
{
    var searchDirectory = currentDirectory;
    while (searchDirectory != null)
    {
        var possiblePluginsDir = Path.Combine(searchDirectory, "Plugins");
        if (Directory.Exists(possiblePluginsDir))
        {
            pluginsDirectory = possiblePluginsDir;
            break;
        }

        searchDirectory = Directory.GetParent(searchDirectory)?.FullName;
    }
}

if (!Directory.Exists(pluginsDirectory))
{
    if (args.Length > 0 && Directory.Exists(args[0]))
    {
        pluginsDirectory = args[0];
    }
    else
    {
        var emptyConfig = JsonSerializer.Serialize(new { DEBUG_PLUGINS = "" });
        var outputPathh = Path.Combine(repoRoot, "submodules", "btcpayserver", "BTCPayServer", "appsettings.dev.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPathh));
        await File.WriteAllTextAsync(outputPathh, emptyConfig);
        return;
    }
}

var pluginPaths = "";
foreach (var plugin in Directory.GetDirectories(pluginsDirectory))
{
    var dllPath = Directory.GetFiles(plugin, "*.dll", SearchOption.AllDirectories)
        .FirstOrDefault(p => p.Contains("net8.0"));

    if (dllPath != null)
        pluginPaths += $"{Path.GetFullPath(dllPath)};";
    else
        Console.WriteLine($"DLL not found for plugin {plugin}");
}

var content = JsonSerializer.Serialize(new { DEBUG_PLUGINS = pluginPaths });
var outputDirectory = Path.Combine(repoRoot, "submodules", "btcpayserver", "BTCPayServer");
var outputPath = Path.Combine(outputDirectory, "appsettings.dev.json");
await File.WriteAllTextAsync(outputPath, content);
