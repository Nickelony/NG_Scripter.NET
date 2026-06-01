using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Model;

/// <summary>
/// An NG command line or a <c>#DEFINE @plugin</c> line as the script parser collected it, before the NG compile
/// reads it.
/// </summary>
/// <param name="CommandLine">The line split into its command and its arguments.</param>
/// <remarks>
/// The command of <see cref="CommandLine"/> keeps its <c>=</c>; a plugin define reads <c>#DEFINE</c> with no arguments.
/// Its <c>Text</c> is the normalized line, and diagnostic spans measure on <c>SourceText</c>, the first physical line
/// as the file holds it, up to its comment.
/// </remarks>
internal sealed record NgLine(ScriptCommandLine CommandLine)
{
	/// <summary>
	/// Gets a value indicating whether the line is a <c>#DEFINE @plugin</c> line rather than a command.
	/// </summary>
	internal bool IsPluginDefine => CommandLine.Command == ScriptLineText.DefineDirective;

	/// <summary>Creates a <c>#DEFINE @plugin</c> line, which has no arguments.</summary>
	/// <param name="text">The normalized line.</param>
	/// <param name="sourceText">The first physical line as the file holds it, up to its comment.</param>
	/// <param name="location">The 1-based line and the full path of the source file.</param>
	internal static NgLine PluginDefine(string text, string sourceText, SourceLocation location) =>
		new(new ScriptCommandLine(text, sourceText, Command: ScriptLineText.DefineDirective, Arguments: [], location));

	/// <summary>
	/// Gets a value indicating whether a line is a <c>#DEFINE @plugin</c> line, which applies where it stands.
	/// </summary>
	/// <param name="line">The normalized line.</param>
	/// <returns><see langword="true"/> when the line opens with <c>#DEFINE</c> and names <c>@plugin</c>.</returns>
	/// <remarks>
	/// VB6 reads such a line by its place among the NG commands
	/// of its section (<c>Module1.bas:5283-5289</c>, <c>13527-13535</c>).
	/// </remarks>
	internal static bool IsPluginDefineText(string line) =>
		ScriptLineText.StartsWithDirective(line, ScriptLineText.DefineDirective) && line.Contains("@plugin", StringComparison.OrdinalIgnoreCase);
}
