namespace TRNGScriptCompiler.Constants;

/// <summary>The values a byte or word field of <c>Script.dat</c> holds, and the words of an error about them.</summary>
internal static class FieldRange
{
	/// <summary>
	/// Reports whether a value fits a field, signed or unsigned: -128 to 255 for a byte and -32768 to 65535 for a word.
	/// </summary>
	/// <remarks>
	/// A width is 1 for a byte or 2 for a word. A value that only wrapping fits is rejected (PD-56), where VB6 passes
	/// -256 to 255 for a byte and -65536 to 65535 for a word (Module1.bas:4915-4937).
	/// </remarks>
	internal static bool Fits(int value, int widthInBytes, out long minimum, out long maximum)
	{
		minimum = -(1L << ((8 * widthInBytes) - 1));
		maximum = (1L << (8 * widthInBytes)) - 1;
		return value >= minimum && value <= maximum;
	}

	/// <summary>Words the finding that a value lies outside the range of its field.</summary>
	/// <param name="value">The value the script gives.</param>
	/// <param name="minimum">The least value the field holds.</param>
	/// <param name="maximum">The greatest value the field holds.</param>
	/// <param name="holder">What holds the values, such as <c>its word</c> or <c>an array byte</c>.</param>
	internal static string DescribeOutside(int value, long minimum, long maximum, string holder) =>
		FormattableString.Invariant($"{value} lies outside {minimum} to {maximum}, the values {holder} holds");

	/// <summary>Words the error for an argument whose value lies outside the range of its field.</summary>
	/// <param name="command">The command as the author wrote it or the definition names it.</param>
	/// <param name="argumentNumber">The 1-based number of the argument.</param>
	/// <param name="value">The value the script gives.</param>
	/// <param name="minimum">The least value the field holds.</param>
	/// <param name="maximum">The greatest value the field holds.</param>
	/// <param name="holder">What holds the values, such as <c>its word</c> or <c>a room number</c>.</param>
	internal static string DescribeArgument(string command, int argumentNumber, int value, long minimum, long maximum, string holder) =>
		FormattableString.Invariant($"Argument {argumentNumber} of {command} is out of range: {DescribeOutside(value, minimum, maximum, holder)}");
}
