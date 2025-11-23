namespace TRNGScriptCompiler.Models;

/// <summary>
/// Information about a discovered plugin.
/// </summary>
public sealed class PluginInfo
{
    public string Name { get; set; } = string.Empty;
    public int PluginId { get; set; }
    public string ScriptPath { get; set; } = string.Empty;
    public Dictionary<string, int>? Constants { get; set; }
}
