using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Tests.Kernel.Text;

[TestClass]
[TestCategory("Fast")]
public sealed class Vb6AnsiTests
{
	[TestMethod]
	[DataRow((byte)0x41, 'A')]
	[DataRow((byte)0x93, '\u201C')]
	[DataRow((byte)0xE9, '\u00E9')]
	[DataRow((byte)0x80, '\u20AC')]
	public void ToChar_GivesTheWindows1252Character(byte value, char expected)
	{
		// VB6's Chr() decodes a \xNN escape through the ANSI code page (Module1.bas:2222), so \x93 is the left double quote.
		Assert.AreEqual(expected, Vb6Ansi.ToChar(value));
	}

	[TestMethod]
	public void EveryByte_WritesBackAsItself()
	{
		// A byte Windows-1252 leaves undefined, such as 0x81, still reads and writes back unchanged, as VB6's ANSI round trip does.
		for (int value = 0; value <= byte.MaxValue; value++)
			Assert.AreSequenceEqual(new[] { (byte)value }, Vb6Ansi.GetBytes(Vb6Ansi.ToChar((byte)value).ToString()), $"Byte 0x{value:X2}");
	}

	[TestMethod]
	public void IsLossy_ReportsACharacterWindows1252CannotHold()
	{
		Assert.IsFalse(Vb6Ansi.IsLossy("Caf\u00E9"), "'\u00E9' is representable in Windows-1252.");
		Assert.IsTrue(Vb6Ansi.IsLossy("Caf\u00E9 \u2192"), "'\u2192' is not representable in Windows-1252.");
		Assert.IsFalse(Vb6Ansi.IsLossy(string.Empty));
	}
}
