using System.Diagnostics.CodeAnalysis;

namespace TRNGScriptCompiler.Model;

/// <summary>Identifies the argument shape expected by an NG command definition.</summary>
/// <remarks>
/// Exactly one value is assigned per argument slot.
/// </remarks>
[SuppressMessage(
	"Naming",
	"CA1720:Identifier contains type name",
	Justification = "The members mirror the argument type tokens of scripter_constants.txt, such as STRING and LONG."
)]
internal enum NgArgumentType
{
	None = 0,

	/// <summary>Language string reference, encoded as one word (its language-table index).</summary>
	String = 1,

	/// <summary>
	/// Byte array encoded as a count byte plus the values, padded to a word boundary and packed little-endian two bytes
	/// per word.
	/// </summary>
	ArrayByte = 2,

	/// <summary>Single 16-bit word value.</summary>
	Word = 3,

	/// <summary>Single 16-bit signed integer.</summary>
	Integer = 4,

	/// <summary>32-bit value encoded as two words (low word first).</summary>
	Long = 5,

	/// <summary>Moveable or item slot number, encoded as one word.</summary>
	ItemSlot = 6,

	/// <summary>
	/// Boolean switch: the command contributes no payload words; ENABLED/DISABLED sets an option or level flag instead.
	/// </summary>
	Bool = 7,

	/// <summary>Array of 16-bit word values (one word per element).</summary>
	Array = 8,

	/// <summary>
	/// Array of 4-bit values, two per byte (low nibble first), packed into words like <see cref="ArrayByte"/>.
	/// </summary>
	ArrayNybble = 9,

	/// <summary>Import-file index resolved through the import-file section, encoded as one word.</summary>
	Import = 10,

	/// <summary>Array of 32-bit values (two words per element, low word first).</summary>
	ArrayLong = 11,
}
