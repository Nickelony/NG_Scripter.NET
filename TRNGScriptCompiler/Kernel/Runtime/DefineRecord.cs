using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Kernel.Runtime;

/// <summary>
/// Holds a <c>#DEFINE</c> constant, scoped to the file that declared it, with its name, value and line.
/// </summary>
internal sealed record DefineRecord
{
	/// <summary>Gets the numeric value assigned by the <c>#DEFINE</c> directive.</summary>
	internal required int Value { get; init; }

	internal required string Name { get; init; }

	/// <summary>
	/// Gets the 1-based line of the directive, with the full path of the file where the define was declared.
	/// </summary>
	internal required SourceLocation Location { get; init; }
}
