namespace TRNGScriptCompiler.Plugins;

/// <summary>A plugin that a <c>Plugin=</c> line of the script declares.</summary>
internal sealed record PluginInfo
{
	/// <summary>Gets the plugin name used in script references and discovery.</summary>
	internal string Name { get; init; } = string.Empty;

	/// <summary>Gets the ID the <c>Plugin=</c> line gives the plugin.</summary>
	internal int Id { get; init; }
}
