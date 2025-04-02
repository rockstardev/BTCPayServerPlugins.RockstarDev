using System.Reflection;
using System.Text.Json;

var currentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

var repoRoot = Path.GetFullPath(Path.Combine(currentDirectory, ".."));
while (!Directory.Exists(Path.Combine(repoRoot, ".git")) &&
       !Directory.Exists(Path.Combine(repoRoot, ".github")) &&
       Directory.GetParent(repoRoot) != null)
{
    repoRoot = Directory.GetParent(repoRoot).FullName;
}
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
    try
    {
        var assemblyConfigurationAttribute = typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
        var buildConfigurationName = assemblyConfigurationAttribute?.Configuration ?? "Debug";

        var binPath = Path.Combine(plugin, "bin");
        if (!Directory.Exists(binPath))
        {
            continue;
        }

        var pluginName = Path.GetFileName(plugin);
        var dllPath = Path.Combine(plugin, "bin", buildConfigurationName, "net8.0", $"{pluginName}.dll");

        if (!File.Exists(dllPath))
        {
            foreach (var configDir in Directory.GetDirectories(binPath))
            {
                var possibleDllPath = Path.Combine(configDir, "net8.0", $"{pluginName}.dll");
                if (File.Exists(possibleDllPath))
                {
                    dllPath = possibleDllPath;
                    break;
                }
            }
        }

        if (File.Exists(dllPath))
        {
            pluginPaths += $"{Path.GetFullPath(dllPath)};";
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"Error processing plugin {plugin}: {e.Message}");
    }
}

var content = JsonSerializer.Serialize(new { DEBUG_PLUGINS = pluginPaths });
var outputDirectory = Path.Combine(repoRoot, "submodules", "btcpayserver", "BTCPayServer");
var outputPath = Path.Combine(outputDirectory, "appsettings.dev.json");
await File.WriteAllTextAsync(outputPath, content);