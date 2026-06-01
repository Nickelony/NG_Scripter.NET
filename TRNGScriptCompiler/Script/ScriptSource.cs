using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// The non-blank lines of <c>Script.txt</c>, each <c>#INCLUDE</c> expanded in place, normalized and split,
/// plus every file's <c>Plugin=</c> lines.
/// </summary>
/// <remarks>
/// <see cref="Files"/> lists files in reading order, twice for one included twice; a set <see cref="ReadError"/>
/// stopped the reading after the lines. Each command line is normalized once, while the loader normalizes
/// an <c>#INCLUDE</c> line again to read its name and <c>DefineRecorder</c> normalizes each <c>#DEFINE</c> line
/// of the raw file text.
/// </remarks>
internal sealed record ScriptSource(
	IReadOnlyList<ScriptCommandLine> Lines,
	IReadOnlyList<(string FilePath, IReadOnlyList<string> Lines)> Files,
	IReadOnlyList<NgLine> PluginLines,
	ScriptReadError? ReadError
);
