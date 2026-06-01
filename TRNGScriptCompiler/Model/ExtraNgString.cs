using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Model;

/// <summary>Holds one ExtraNG string of a language file: its index, its text and where the file declares it.</summary>
internal sealed record ExtraNgString
{
	/// <summary>Gets the ExtraNG string index written into the language header.</summary>
	internal required int Index { get; init; }

	internal required string Text { get; init; }

	/// <summary>
	/// Gets the 1-based line of the entry in its language file, or no line for an entry read from no line.
	/// </summary>
	internal SourceLocation Location { get; init; }
}
