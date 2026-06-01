using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>One section line with its place as written.</summary>
/// <remarks>
/// The command in <see cref="CommandLine"/> keeps its trailing <c>=</c>. <see cref="SourceIndex"/> is the line's
/// place as written, before the emission sort.
/// </remarks>
internal readonly record struct ParsedClassicLine(ScriptCommandLine CommandLine, int SourceIndex);
