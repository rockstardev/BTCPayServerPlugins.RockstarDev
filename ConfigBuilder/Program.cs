using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

// 1) Figure out repo root and Plugins folder (allow passing a custom path via args[0])
var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
var repoRoot = GetRepoRoot(exeDir);
var pluginsDir = GetPluginsDir(repoRoot, exeDir, args);

// 2) If Plugins folder still not found, write empty config and exit
if (pluginsDir is null)
{
    await WriteConfig(repoRoot, "");
    return;
}

// 3) Build DEBUG_PLUGINS value by resolving each plugin's DLL
var pluginPaths = new List<string>();
foreach (var plugin in Directory.GetDirectories(pluginsDir))
{
    var dll = GetPluginDll(plugin);
    if (!string.IsNullOrEmpty(dll))
        pluginPaths.Add(Path.GetFullPath(dll));
    else
        Console.WriteLine($"DLL not found for plugin: {plugin}");
}

// 4) Write appsettings.dev.json
await WriteConfig(repoRoot, string.Join(';', pluginPaths));


// ----------------------------- Utility Functions -----------------------------

static string GetOutputPath(string repoRoot)
{
    return Path.Combine(repoRoot, "submodules", "btcpayserver", "BTCPayServer", "appsettings.dev.json");
}

static async Task WriteConfig(string repoRoot, string pluginPaths)
{
    var outputPath = GetOutputPath(repoRoot);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    var content = JsonSerializer.Serialize(new { DEBUG_PLUGINS = pluginPaths });
    await File.WriteAllTextAsync(outputPath, content);
}

static string GetRepoRoot(string startDir)
{
    var cur = Path.GetFullPath(Path.Combine(startDir, ".."));
    while (cur is not null)
    {
        if (Directory.Exists(Path.Combine(cur, ".git")) || Directory.Exists(Path.Combine(cur, ".github")))
            return cur;
        var parent = Directory.GetParent(cur);
        if (parent is null) break;
        cur = parent.FullName;
    }
    return startDir; // fallback
}

static string? GetPluginsDir(string repoRoot, string exeDir, string[] args)
{
    // Prefer explicit arg if valid
    if (args.Length > 0 && Directory.Exists(args[0]))
        return args[0];

    // Repo-default
    var byRepo = Path.Combine(repoRoot, "Plugins");
    if (Directory.Exists(byRepo))
        return byRepo;

    // Walk up from exeDir to find a "Plugins" folder
    var cur = exeDir;
    while (!string.IsNullOrEmpty(cur))
    {
        var candidate = Path.Combine(cur, "Plugins");
        if (Directory.Exists(candidate))
            return candidate;
        cur = Directory.GetParent(cur)?.FullName ?? "";
    }

    // Not found
    return null;
}

static string? GetPluginDll(string pluginDir)
{
    // Find the .csproj, get assembly name + target frameworks
    var csproj = Directory.GetFiles(pluginDir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
    if (csproj is null)
        return FindAnyNet8Dll(pluginDir);

    var (assemblyName, tfms) = ParseCsproj(csproj);
    if (tfms.Count == 0) tfms.Add("net8.0"); // default if unspecified

    // Prefer net8.0 if present
    tfms = tfms
        .OrderByDescending(t => string.Equals(t, "net8.0", StringComparison.OrdinalIgnoreCase))
        .ThenBy(t => t)
        .ToList();

    var bin = Path.Combine(Path.GetDirectoryName(csproj)!, "bin");
    foreach (var tfm in tfms)
    {
        // Try Debug then Release
        var debug = Path.Combine(bin, "Debug", tfm, $"{assemblyName}.dll");
        if (File.Exists(debug)) return debug;

        var release = Path.Combine(bin, "Release", tfm, $"{assemblyName}.dll");
        if (File.Exists(release)) return release;
    }

    // Fallbacks
    var byName = Directory.GetFiles(pluginDir, $"{assemblyName}.dll", SearchOption.AllDirectories)
        .FirstOrDefault(p => p.Contains($"{Path.DirectorySeparatorChar}net8.0{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrEmpty(byName)) return byName;

    return FindAnyNet8Dll(pluginDir);
}

static (string assemblyName, List<string> tfms) ParseCsproj(string csprojPath)
{
    var doc = XDocument.Load(csprojPath);
    var pg = doc.Root?.Elements("PropertyGroup");

    var assemblyName =
        pg?.Elements("AssemblyName").FirstOrDefault()?.Value
        ?? Path.GetFileNameWithoutExtension(csprojPath);

    var tfms = new List<string>();
    var single = pg?.Elements("TargetFramework").FirstOrDefault()?.Value;
    var multi = pg?.Elements("TargetFrameworks").FirstOrDefault()?.Value;

    if (!string.IsNullOrWhiteSpace(multi))
        tfms.AddRange(multi.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    else if (!string.IsNullOrWhiteSpace(single))
        tfms.Add(single);

    return (assemblyName, tfms);
}

static string? FindAnyNet8Dll(string dir)
{
    return Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories)
        .FirstOrDefault(p => p.Contains($"{Path.DirectorySeparatorChar}net8.0{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
}
