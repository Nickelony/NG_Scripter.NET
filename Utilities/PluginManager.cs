using TRNGScriptCompiler.Models;

namespace TRNGScriptCompiler.Utilities;

/// <summary>
/// Manages TRNG plugin discovery and constant loading.
/// </summary>
public class PluginManager
{
    private readonly CompilerGlobals _globals;
    private readonly List<PluginInfo> _discoveredPlugins = [];

    public PluginManager(CompilerGlobals globals)
        => _globals = globals;

    /// <summary>
    /// Discovers all plugin*.script files in the specified directory.
    /// </summary>
    public bool DiscoverPlugins(string searchDirectory)
    {
        try
        {
            Logger.LogVerboseIf(_globals.Verbose, "Searching for plugin files...");

            if (!Directory.Exists(searchDirectory))
            {
                Logger.LogVerbose($"Plugin search directory does not exist: {searchDirectory}");
                return true; // Not an error - just no plugins
            }

            // Find all plugin*.script files
            var pluginFiles = Directory.GetFiles(searchDirectory, "Plugin*.script", SearchOption.TopDirectoryOnly);

            if (pluginFiles.Length == 0)
            {
                Logger.LogVerboseIf(_globals.Verbose, "No plugin files found.");
                return true;
            }

            Logger.Log($"Found {pluginFiles.Length} plugin file(s)");

            // Sort by file date (oldest first)
            var sortedFiles = pluginFiles
                .Select(f => new { Path = f, Date = File.GetLastWriteTime(f) })
                .OrderBy(f => f.Date)
                .Select(f => f.Path)
                .ToList();

            // Load each plugin
            foreach (var pluginFile in sortedFiles)
            {
                var pluginName = Path.GetFileNameWithoutExtension(pluginFile);

                var plugin = new PluginInfo
                {
                    Name = pluginName,
                    ScriptPath = pluginFile
                };

                Logger.LogVerbose($"Discovered plugin: {pluginName}");
                _discoveredPlugins.Add(plugin);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error discovering plugins: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads constants from plugin script files.
    /// </summary>
    public bool LoadPluginConstants(PluginInfo plugin)
    {
        try
        {
            Logger.LogVerbose($"Loading plugin constants: {plugin.Name}");

            if (!File.Exists(plugin.ScriptPath))
            {
                Logger.LogWarning($"Plugin script file not found: {plugin.ScriptPath}");
                return false;
            }

            var lines = File.ReadAllLines(plugin.ScriptPath);
            var constants = new Dictionary<string, int>();
            bool inConstantsSection = false;

            // Parse constants from plugin file
            // Format: <START_CONSTANTS> ... NAME:VALUE ... <END>
            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Check for section markers
                if (trimmed.Equals("<START_CONSTANTS>", StringComparison.OrdinalIgnoreCase))
                {
                    inConstantsSection = true;
                    continue;
                }

                if (trimmed.Equals("<END>", StringComparison.OrdinalIgnoreCase))
                {
                    inConstantsSection = false;
                    continue;
                }

                // Skip if not in constants section
                if (!inConstantsSection)
                    continue;

                // Skip comments and empty lines
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';'))
                    continue;

                // Parse NAME:VALUE format
                var colonPos = trimmed.IndexOf(':');

                if (colonPos > 0)
                {
                    var constantName = trimmed[..colonPos].Trim();
                    var remainder = trimmed[(colonPos + 1)..];

                    // Remove any inline comments (after semicolon)
                    var commentPos = remainder.IndexOf(';');

                    var valueStr = commentPos >= 0
                        ? remainder[..commentPos].Trim()
                        : remainder.Trim();

                    if (!string.IsNullOrEmpty(constantName) && !string.IsNullOrEmpty(valueStr)
                        && TryParseConstantValue(valueStr, out int value))
                    {
                        constants[constantName] = value;
                        Logger.LogVerboseIf(_globals.Verbose, $"  {constantName} = {value}");
                    }
                }
            }

            plugin.Constants = constants;
            Logger.LogVerbose($"Loaded {constants.Count} constant(s) from {plugin.Name}");

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error loading plugin constants from {plugin.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets all discovered plugins.
    /// </summary>
    public List<PluginInfo> GetDiscoveredPlugins()
        => _discoveredPlugins;

    /// <summary>
    /// Finds a plugin by name.
    /// </summary>
    public PluginInfo? FindPlugin(string pluginName)
    {
        return _discoveredPlugins.FirstOrDefault(p =>
            p.Name.Equals(pluginName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves a constant value from plugin constants.
    /// </summary>
    public bool TryResolvePluginConstant(string pluginName, string constantName, out int value)
    {
        value = 0;
        var plugin = FindPlugin(pluginName);

        if (plugin is null || plugin.Constants is null)
            return false;

        return plugin.Constants.TryGetValue(constantName, out value);
    }

    private static bool TryParseConstantValue(string valueStr, out int value)
    {
        // Handle hex values
        if (valueStr.StartsWith('$'))
            return int.TryParse(valueStr[1..], System.Globalization.NumberStyles.HexNumber, null, out value);
        if (valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(valueStr[2..], System.Globalization.NumberStyles.HexNumber, null, out value);

        // Handle decimal values
        return int.TryParse(valueStr, out value);
    }
}
