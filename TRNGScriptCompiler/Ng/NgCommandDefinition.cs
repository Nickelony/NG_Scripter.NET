using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Ng;

/// <summary>Definition of an NG command (from scripter_constants.txt).</summary>
internal sealed record NgCommandDefinition
{
	/// <summary>Gets the canonical NG command name.</summary>
	internal required string Name { get; init; }

	/// <summary>Gets the NG command code emitted into the compiled header.</summary>
	internal required NgCommandCode Code { get; init; }

	/// <summary>
	/// Gets the argument types of the command, in order; an array type is the last, and takes the remaining values.
	/// </summary>
	internal required IReadOnlyList<NgArgumentType> ArgumentTypes { get; init; }

	/// <summary>
	/// Gets a value indicating whether the command belongs in the <c>[Options]</c> section alone, which the definition
	/// marks with a leading <c>*</c> on its name.
	/// </summary>
	internal bool IsOptionsOnly { get; init; }

	/// <summary>
	/// Gets the maximum number of times the command may appear in one section, or <c>-1</c> for unlimited.
	/// </summary>
	internal int MaxOccurrences { get; init; } = -1;

	/// <summary>
	/// Gets a value indicating whether the command is a flag: its one argument is a <c>Bool</c>, so it sets an option
	/// or level switch and writes no words.
	/// </summary>
	internal bool IsFlag => ArgumentTypes.Count == 1 && ArgumentTypes[0] == NgArgumentType.Bool;

	/// <summary>Gets the flag value emitted when the command is enabled in boolean form.</summary>
	internal int BoolEnabledValue { get; init; }

	/// <summary>Gets the flag value emitted when the command is disabled in boolean form.</summary>
	internal int BoolDisabledValue { get; init; }
}
