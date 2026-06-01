using TRNGScriptCompiler.Language;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Constants;

/// <summary>
/// One numeric reader serves classic and NG arguments, <c>#DEFINE</c> values, options,
/// and <c>File=</c> and <c>!NNN</c> indexes.
/// </summary>
[TestClass]
public sealed class NumericArgumentTests
{
	// The command is the seventh line of the script.
	private const int CommandLine = 7;

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Fog= 5+-3,0,0", "Argument 1 of Fog= is invalid: 5+-3 has an operator with no value after it; write 5-3")]
	[DataRow("LevelFarView= -", "Argument 1 of LevelFarView= is invalid: - has an operator with no value after it; write a number")]
	[DataRow(
		"LevelFarView= &HFFFF",
		"Argument 1 of LevelFarView= is invalid: VB reads &HFFFF as -1; write $FFFF for 65535, or -1 for the value VB reads"
	)]
	[DataRow("Fog= 1.5,0,0", "Argument 1 of Fog= is invalid: 1.5 is not a whole number; write it without a fraction, a separator or an exponent")]
	[DataRow("LevelFarView= 0x10", "Argument 1 of LevelFarView= is invalid: 0x10 is not a number; write $10 for a hexadecimal value")]
	[DataRow(
		"LevelFarView= $ 12",
		"Argument 1 of LevelFarView= is invalid: $ 12 is not a hexadecimal number; write 1 to 8 hexadecimal digits after $"
	)]
	[DataRow("Mirror= 0,2147483647+1", "Argument 2 of Mirror= is invalid: 2147483647+1 overflows 32 bits")]
	public void NumberThatReadsWrong_FailsNamingTheFix(string command, string expectedMessage)
	{
		// PD-51: VB6 reads 5+-3 as 7 and a lone - as 0, because it reuses the previous value for an empty piece (Module1.bas:4857-4911).
		// Val reads &HFFFF as -1 and 1.5 as 2 through the Windows locale. IsNumeric and the hexadecimal check reject 0x10 and "$ 12"
		// (MioModulo.bas:4986-5011), and a sum past a Long raises error 6.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + command + "\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);
		Assert.AreEqual((expectedMessage, CommandLine), (diagnostic.Message, diagnostic.LineNumber));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void HexadecimalEscapeWithAMinusSign_Fails()
	{
		// PD-51: VB6's hexadecimal check accepts a - in the first place (MioModulo.bas:4986-5011), so it decodes \x-4.
		Assert.IsFalse(LanguageEscapeCodec.TryDecodeClassic("Hello \\x-4", out _, out _));

		using var workspace = TestWorkspace.CreateCompilable(languageContent: "[Strings]\nTest Level\nHello \\x-4\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 3,
			message: "The line 'Hello \\x-4' holds an invalid hexadecimal escape; follow each \\x with two hexadecimal digits, such as \\x41",
			file: workspace.PathOf(Path.Combine("script", "English.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Fog= ENABLED,PISTOLS_ANIM+1,PLANT1+2", "Fog= 1,2,3")]
	[DataRow("Fog= &H10,&O17,$FF", "Fog= 16,15,255")]
	[DataRow("Fog= -1+2,MY_VALUE,#3", "Fog= 1,7,3")]
	public void ClassicNumericArgument_TakesConstantsSlotAndStaticNamesAndVbLiterals(string command, string numericCommand)
	{
		// VB6 reads a classic numeric argument with GetArgNumerico, which looks a name up among the constants, the slot names, the
		// defines and the static names (Module1.bas:4886-4897, 10117-10170), and reads &H and &O through Val.
		Assert.AreSequenceEqual(CompileScriptDat(numericCommand), CompileScriptDat(command));
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("File= $0, English.txt")]
	[DataRow("File= FIRST_LANGUAGE, English.txt")]
	[DataRow("File= 1-1, English.txt")]
	public void LanguageFileIndex_TakesTheFormsOfANumericArgument(string fileLine)
	{
		// VB6 reads the File= index with GetArgNumerico (Module1.bas:5493), so $0, a define and a sum name index 0.
		using var numeric = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level);
		using var written = TestWorkspace.CreateCompilable("#DEFINE FIRST_LANGUAGE 0\n[Language]\n" + fileLine + "\n" + TestScripts.Level);

		TestCompileResult numericResult = numeric.Compile();
		TestCompileResult writtenResult = written.Compile();

		Assert.IsTrue(numericResult.Success, numericResult.Log);
		Assert.IsTrue(writtenResult.Success, writtenResult.Log);

		Assert.AreSequenceEqual(numeric.ReadScriptDat(), written.ReadScriptDat());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LanguageFileIndexThatReadsWrong_Fails()
	{
		using var workspace = TestWorkspace.CreateCompilable("[Language]\nFile= 0+, English.txt\n" + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		Assert.AreEqual(
			"The index of the language file is invalid: 0+ has an operator with no value after it; write 0",
			result.Diagnostics.Single(entry => entry.IsError).Message
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("!$64")]
	[DataRow("!MY_ID")]
	[DataRow("!50+50")]
	public void ExtraNgStringIndex_TakesTheFormsOfANumericArgument(string reference)
	{
		// VB6 reads the index of a !NNN reference with GetArgNumerico (Module1.bas:6535-6540), so !$64 and !MY_ID name string 100.
		const string language = "[Strings]\nTest Level\n\n[ExtraNG]\n100: Damage Text\n";

		using var numeric = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + "Damage= 1,2,3,4,!100,5\n", language);

		using var written = TestWorkspace.CreateCompilable(
			"#DEFINE MY_ID 100\n" + TestScripts.Language + TestScripts.Level + $"Damage= 1,2,3,4,{reference},5\n",
			language
		);

		TestCompileResult numericResult = numeric.Compile();
		TestCompileResult writtenResult = written.Compile();

		Assert.IsTrue(numericResult.Success, numericResult.Log);
		Assert.IsTrue(writtenResult.Success, writtenResult.Log);

		Assert.AreSequenceEqual(numeric.ReadScriptDat(), written.ReadScriptDat());
	}

	private static byte[] CompileScriptDat(string command) =>
		TestWorkspace.CompileScriptDatOnStockObjectsH("#DEFINE MY_VALUE 7\n" + TestScripts.Language + TestScripts.Level + command + "\n");
}
