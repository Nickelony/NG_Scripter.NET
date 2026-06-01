namespace TRNGScriptCompiler.Model;

/// <summary>Tests the shape of an <see cref="NgArgumentType"/>.</summary>
internal static class NgArgumentTypeExtensions
{
	/// <summary>Returns whether the type is one of the known array types.</summary>
	internal static bool IsArray(this NgArgumentType type) =>
		type is NgArgumentType.Array or NgArgumentType.ArrayByte or NgArgumentType.ArrayNybble or NgArgumentType.ArrayLong;
}
