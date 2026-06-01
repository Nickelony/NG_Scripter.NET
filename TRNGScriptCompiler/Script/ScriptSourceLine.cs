using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// One source line produced by the script source loader after include handling and line continuation.
/// </summary>
/// <param name="Text">The raw line text after include expansion and line continuation.</param>
/// <param name="SourceText">
/// The first physical line as the file holds it, up to its comment, which a diagnostic span measures on.
/// </param>
/// <param name="Location">The 1-based line number within the source file, and the full path of that file.</param>
internal sealed record ScriptSourceLine(string Text, string SourceText, SourceLocation Location);
