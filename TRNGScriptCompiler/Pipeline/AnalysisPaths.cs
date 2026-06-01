namespace TRNGScriptCompiler.Pipeline;

/// <summary>Validated paths extracted from <see cref="TRNGCompilerOptions"/> for a compilation analysis run.</summary>
/// <remarks>
/// <c>PluginDirectory</c> is the folder scanned for plugin scripts, and <c>ScriptFile</c> is the resolved
/// <c>Script.txt</c> path.
/// </remarks>
internal sealed record AnalysisPaths(
	string EngineDirectory,
	string PluginDirectory,
	string ScriptDirectory,
	string ScriptFile,
	string ConstantsFilePath
);
