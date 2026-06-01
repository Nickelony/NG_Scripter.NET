namespace TRNGScriptCompiler.Ng;

/// <summary>Names the arguments that make a second line of an NG command in one section a duplicate.</summary>
/// <remarks>
/// VB6 lists the codes in <c>VetNoId</c>, <c>VetIgnora</c> and <c>VetDoppi</c> (<c>Module1.bas:1236-1247</c>).
/// </remarks>
internal enum NgDuplicateRule
{
	/// <summary>A line whose first argument repeats an earlier line's is a duplicate.</summary>
	FirstArgument,

	/// <summary>Any second line is a duplicate, for a code VB6 lists in <c>VetNoId</c> alone.</summary>
	OnePerSection,

	/// <summary>
	/// A line whose second argument repeats an earlier line's is a duplicate, whatever its first argument.
	/// </summary>
	/// <remarks>
	/// VB6 lists these codes in both <c>VetNoId</c> and <c>VetDoppi</c>.
	/// </remarks>
	SecondArgument,

	/// <summary>
	/// A line whose first two arguments repeat an earlier line's is a duplicate, for a code VB6 lists
	/// in <c>VetDoppi</c> alone.
	/// </summary>
	FirstTwoArguments,

	/// <summary>
	/// A line whose slot, the first argument, and animation index, the third, repeat an earlier line's is a duplicate.
	/// </summary>
	SlotAndAnimation,

	/// <summary>No line is a duplicate, for a code VB6 lists in <c>VetIgnora</c>.</summary>
	Exempt,
}
