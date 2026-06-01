using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Constants;

/// <summary>The <c>NAME:VALUE</c> grammar that the constants file and the plugin scripts share (PD-62).</summary>
[TestClass]
public sealed class ConstantsFileGrammarTests
{
	private const string FilePath = @"C:\Engine\Plugin_A.script";

	private static (IReadOnlyDictionary<string, ConstantDefinition> Constants, IReadOnlyList<CompilerDiagnostic> Diagnostics) Parse(
		params string[] constantLines
	)
	{
		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));
		string[] lines = ["<START_CONSTANTS>", .. constantLines, "<END>"];

		return (ConstantsFileParser.Parse(lines, FilePath, diagnostics), diagnostics.Recorded);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("MY_VALUE: $10", 16)]
	[DataRow("MY_VALUE:$10", 16)]
	[DataRow("VIEW_USE_YARDS\t:$0080", 128)]
	[DataRow("MY_VALUE:-5   ;a comment", -5)]
	[DataRow("MY_VALUE:&H7400", 29696)]
	[DataRow("MY_VALUE:&HFFFF&", 65535)]
	[DataRow("MY_VALUE:&H8000&", 32768)]
	[DataRow("MY_VALUE:&O17&", 15)]
	[DataRow("MY_VALUE:-2147483648", int.MinValue)]
	public void Value_IsReadAsOneNumber(string line, int expected)
	{
		// VB6 reads $ hexadecimal only right after the colon and the rest with Val (Module1.bas:3129-3135), so " $10" is 0 there, Val reads
		// VB's & Long suffix, so &HFFFF& is 65535, and it keeps a tab before the colon in the name (:3094, 3126), so VIEW_USE_YARDS never
		// resolves (PD-62).
		(IReadOnlyDictionary<string, ConstantDefinition> constants, IReadOnlyList<CompilerDiagnostic> diagnostics) = Parse(line);

		DiagnosticAssert.NoDiagnostics(diagnostics);
		Assert.AreEqual(expected, constants[line.Split(':')[0].Trim()].Value);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("MY_VALUE:12abc", "The value of the constant MY_VALUE is invalid: 12abc is not a number; a decimal number holds digits only")]
	[DataRow(
		"MY_VALUE:&H8000",
		"The value of the constant MY_VALUE is invalid: VB reads &H8000 as -32768; write $8000 for 32768, or -32768 for the value VB reads"
	)]
	[DataRow("MY_VALUE:#10", "The value of the constant MY_VALUE is invalid: #10 is not a number here; write $10 for a hexadecimal value")]
	[DataRow("MY_VALUE:0x10", "The value of the constant MY_VALUE is invalid: 0x10 is not a number; write $10 for a hexadecimal value")]
	[DataRow("MY_VALUE:", "The value of the constant MY_VALUE is invalid: the value is empty")]
	[DataRow(
		"MY_VALUE:&HFFFFFFFF&",
		"The value of the constant MY_VALUE is invalid: VB reads &HFFFFFFFF& as -1; write $FFFFFFFF for 4294967295, or -1 for the value VB reads"
	)]
	[DataRow("MY_VALUE:65535&", "The value of the constant MY_VALUE is invalid: 65535& is not a number; a decimal number holds digits only")]
	[DataRow(
		"MY_VALUE:$FF&",
		"The value of the constant MY_VALUE is invalid: $FF& is not a hexadecimal number; write 1 to 8 hexadecimal digits after $"
	)]
	public void ValueThatReadsWrong_FailsNamingTheFileAndTheLine(string line, string expectedMessage)
	{
		// VB6 reads 12abc as 12, &H8000 as -32768, and #10, 0x10 and an empty value as 0 with Val (Module1.bas:3129-3135); Val stops at
		// the & of 65535& and $FF&. The port takes VB's & Long suffix after an &H or &O value alone.
		(IReadOnlyDictionary<string, ConstantDefinition> constants, IReadOnlyList<CompilerDiagnostic> diagnostics) = Parse("FIRST:1", line);

		CompilerDiagnostic diagnostic = diagnostics.Single();

		Assert.AreEqual((true, expectedMessage, 3, FilePath), (diagnostic.IsError, diagnostic.Message, diagnostic.LineNumber, diagnostic.SourceFile));
		Assert.IsFalse(constants.ContainsKey("MY_VALUE"));
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow(
		"NO_COLON 5",
		"The constants line 'NO_COLON 5' is skipped, because a constant takes one ':' between its name and its value; write NAME:VALUE"
	)]
	[DataRow("A:1:2", "The constants line 'A:1:2' is skipped, because a constant takes one ':' between its name and its value; write NAME:VALUE")]
	[DataRow(
		"<END_CONSTANTS>",
		"The constants line '<END_CONSTANTS>' is skipped, because a constant takes one ':' between its name and its value; write NAME:VALUE"
	)]
	[DataRow("1ST:5", "The constant name '1ST' is skipped, because a name starts with a letter or _ and holds no space")]
	[DataRow("TWO WORDS:5", "The constant name 'TWO WORDS' is skipped, because a name starts with a letter or _ and holds no space")]
	public void LineThatIsNoConstant_WarnsAndTheSectionGoesOn(string line, string expectedMessage)
	{
		// VB6 shows a message box for such a line and reads on (Module1.bas:3094-3119), and only <END> closes the section (:2579).
		(IReadOnlyDictionary<string, ConstantDefinition> constants, IReadOnlyList<CompilerDiagnostic> diagnostics) = Parse(line, "AFTER:7");

		CompilerDiagnostic diagnostic = diagnostics.Single();

		Assert.AreEqual((false, expectedMessage, 2), (diagnostic.IsError, diagnostic.Message, diagnostic.LineNumber));
		Assert.AreEqual(7, constants["AFTER"].Value);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void NameGivenTwice_KeepsTheFirstDefinitionAndWarns()
	{
		// VB6 keeps both entries, and its lookup finds the first (Module1.bas:10116-10127).
		(IReadOnlyDictionary<string, ConstantDefinition> constants, IReadOnlyList<CompilerDiagnostic> diagnostics) = Parse("TWICE:1", "twice:2");

		Assert.AreEqual(new ConstantDefinition(Value: 1, Line: 2), constants["TWICE"]);
		Assert.AreEqual("The constant twice repeats line 2, whose value is used; remove one of the two lines", diagnostics.Single().Message);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LinesOutsideAConstantsSection_AreNotRead()
	{
		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));
		string[] lines = ["OUTSIDE:1", "<START_CONSTANTS>", "INSIDE:2", "<END>", "<START_NEW_COMMANDS>", "2#1#Snow=:Word", "<END>"];

		IReadOnlyDictionary<string, ConstantDefinition> constants = ConstantsFileParser.Parse(lines, FilePath, diagnostics);

		Assert.AreSequenceEqual(["INSIDE"], constants.Keys, SequenceOrder.InAnyOrder);
		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("constants", "ENABLED", 1)]
	[DataRow("plugin", "PLUGIN_DEMO_CONST", 0x40)]
	public void StockFile_ParsesItsConstantsWithoutADiagnostic(string file, string name, int value)
	{
		string path =
			file == "constants"
				? Path.Combine(AppContext.BaseDirectory, CompilerFileNames.ScripterConstants)
				: Path.Combine(TestFixtures.WorkspaceDirectory, "PluginDemo.script");

		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));

		IReadOnlyDictionary<string, ConstantDefinition> constants = ConstantsFileParser.Parse(
			ConstantsFile.Load(path, diagnostics).Lines,
			path,
			diagnostics
		);

		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);
		Assert.AreEqual(value, constants[name].Value);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void PluginScriptValueThatReadsWrong_FailsTheCompile()
	{
		// The plugin scripts share the grammar of the constants file, as VB6 reads both with one loop (Module1.bas:3076-3140).
		using var workspace = TestWorkspace.CreateCompilable();
		workspace.WithPluginFile("Plugin_A.script", "<START_CONSTANTS>\r\nGOOD:$10\r\nBAD:12abc\r\n<END>\r\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);

		Assert.AreEqual(
			("The value of the constant BAD is invalid: 12abc is not a number; a decimal number holds digits only", 3, "Plugin_A.script"),
			(diagnostic.Message, diagnostic.LineNumber, Path.GetFileName(diagnostic.SourceFile))
		);
	}
}
