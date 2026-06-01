using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// The error that stopped the reading of a script's source, which the parse reports after the lines read before it.
/// </summary>
/// <remarks>
/// <c>Location</c> names the line where the reading stopped, or only the file when no line applies.
/// </remarks>
internal sealed record ScriptReadError(string Message, SourceLocation Location, Exception? Exception = null);
