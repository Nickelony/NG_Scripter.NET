using TRNGScriptCompiler.Kernel.Numerics;

namespace TRNGScriptCompiler.Tests.Kernel.Numerics;

[TestClass]
[TestCategory("Fast")]
public sealed class NumericLiteralTests
{
	[TestMethod]
	[DataRow("0", 0)]
	[DataRow("123", 123)]
	[DataRow("007", 7)]
	[DataRow("$10", 16)]
	[DataRow("$ffff", 65535)]
	[DataRow("#10", 16)]
	[DataRow("$FFFFFFFF", -1)]
	[DataRow("$80000000", int.MinValue)]
	[DataRow("&H10", 16)]
	[DataRow("&h7fff", 32767)]
	[DataRow("&H10000", 65536)]
	[DataRow("&H7FFFFFFF", int.MaxValue)]
	[DataRow("&O17", 15)]
	public void TryRead_ReadsTheLiteralsBothCompilersReadAlike(string text, int expected)
	{
		// VB6 reads $ and # as Val("&h" & digits & "&"), a 32-bit Long (Module1.bas:4870-4884), and the rest through Val (:4898-4900).
		Assert.IsTrue(NumericLiteral.TryRead(text, out int value, out string? error), error);
		Assert.AreEqual(expected, value);
	}

	[TestMethod]
	[DataRow("$")]
	[DataRow("$GG")]
	[DataRow("$ 12")]
	[DataRow("$100000000")]
	[DataRow("&HFFFF")]
	[DataRow("&B101")]
	[DataRow("0x10")]
	[DataRow("1.5")]
	[DataRow("12x")]
	public void TryRead_RejectsALiteralThatReadsWrong(string text)
	{
		Assert.IsFalse(NumericLiteral.TryRead(text, out int value, out string? error));
		Assert.AreEqual(0, value);
		Assert.IsNotNull(error);
	}

	[TestMethod]
	[DataRow("12", 12)]
	[DataRow("-12", -12)]
	[DataRow("+12", 12)]
	[DataRow("$82", 130)]
	[DataRow("&H82", 130)]
	[DataRow("&O17", 15)]
	[DataRow("-2147483648", int.MinValue)]
	[DataRow("+2147483647", int.MaxValue)]
	public void TryReadStandalone_ReadsOneNumber(string text, int expected)
	{
		// VB6's Val reads the sign with the digits into a Long, so -2147483648 is read (Module1.bas:2118, 3134).
		Assert.IsTrue(NumericLiteral.TryReadStandalone(text, out int value, out string? error), error);
		Assert.AreEqual(expected, value);
	}

	[TestMethod]
	[DataRow("-2147483649", "-2147483649 does not fit 32 bits")]
	[DataRow("2147483648", "2147483648 does not fit 32 bits")]
	public void TryReadStandalone_RejectsADecimalPast32Bits(string text, string expectedError)
	{
		Assert.IsFalse(NumericLiteral.TryReadStandalone(text, out _, out string? error));
		Assert.AreEqual(expectedError, error);
	}

	[TestMethod]
	[DataRow("&HFFFF&", 65535)]
	[DataRow("&h8000&", 32768)]
	[DataRow("&O17&", 15)]
	public void TryReadStandalone_WithTheLongSuffix_ReadsAsValDoes(string text, int expected)
	{
		// The constants file reads a value with Val alone (Module1.bas:3134), and Val reads &HFFFF& as the Long 65535.
		Assert.IsTrue(NumericLiteral.TryReadStandalone(text, out int value, out string? error, acceptLongSuffix: true), error);
		Assert.AreEqual(expected, value);
	}

	[TestMethod]
	[DataRow("&H10&", "&H10& ends in &, VB's Long suffix, which a number here does not take; write $10 for 16")]
	[DataRow("&O17&", "&O17& ends in &, VB's Long suffix, which a number here does not take; write $F for 15")]
	public void LongSuffix_WhereTheReaderTestsIsNumericFirst_FailsNamingTheSuffix(string text, string expectedError)
	{
		// A script number, an ExtraNG index and a command definition pass IsNumeric before Val (Module1.bas:4887, 2109, 2696).
		Assert.IsFalse(NumericLiteral.TryRead(text, out _, out string? error));
		Assert.AreEqual(expectedError, error);
		Assert.IsFalse(NumericLiteral.TryReadStandalone(text, out _, out error));
		Assert.AreEqual(expectedError, error);
	}

	[TestMethod]
	[DataRow("", "the value is empty")]
	[DataRow("#82", "#82 is not a number here; write $82 for a hexadecimal value")]
	[DataRow("5+3", "5+3 is not a number; a decimal number holds digits only")]
	[DataRow("NAME", "NAME is not a number; write decimal digits with an optional sign, or a $, &H or &O value")]
	[DataRow("-$10", "-$10 is not a number; write decimal digits with an optional sign, or a $, &H or &O value")]
	[DataRow("(12)", "(12) is not a number; write decimal digits with an optional sign, or a $, &H or &O value")]
	public void TryReadStandalone_RejectsANameASumAndAHash(string text, string expectedError)
	{
		// A language file and a constants file have no #DEFINE scope; VB6 reads #82, -$10 and (12) there as 0 with Val
		// (Module1.bas:2118, 3129-3135).
		Assert.IsFalse(NumericLiteral.TryReadStandalone(text, out _, out string? error));
		Assert.AreEqual(expectedError, error);
	}
}
