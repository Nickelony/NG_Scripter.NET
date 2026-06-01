namespace TRNGScriptCompiler.Plugins;

/// <summary>A <c>Plugin*.script</c> file that discovery found and read.</summary>
/// <remarks>
/// <c>Name</c> is the file name without its extension, and <c>Path</c> is the file's full path.
/// </remarks>
internal sealed record PluginScript(string Name, string Path, IReadOnlyList<string> Lines);
