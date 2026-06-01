using System.Globalization;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Kernel.Diagnostics;

[TestClass]
public sealed class DiagnosticMessageTests
{
	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Layer1= 1,2,300,4", "Argument 3 of Layer1= is out of range: 300 lies outside -128 to 255, the values its byte holds")]
	[DataRow("Layer1= 1,2,x,4", "Argument 3 of Layer1= is invalid: x is neither a number nor a known name")]
	[DataRow("Fog= 1,2,-3", "Argument 3 of Fog= is out of range: -3 lies outside 0 to 255, the values a color holds")]
	[DataRow("LensFlare= 0,0,0,0,0,x", "Argument 6 of LensFlare= is invalid: x is neither a number nor a known name")]
	[DataRow(
		"Key= 1,Test Level,0,0,0,0,0,70000",
		"Argument 8 of Key= is out of range: 70000 lies outside -32768 to 65535, the values its word holds"
	)]
	public void ClassicArgumentNumbers_CountFromOne(string commandLine, string expectedMessage)
	{
		// VB6 numbers an argument from 1 in its messages (GetStringa(664, CStr(Indice + 1)), Module1.bas:4819).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + commandLine + "\n");

		TestCompileResult result = workspace.Compile();

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);
		Assert.AreEqual((expectedMessage, 7), (diagnostic.Message, diagnostic.LineNumber));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LensFlareCoordinate_IsShownAsWritten()
	{
		// VB6 writes the coordinate divided by 256 as a word (Module1.bas:6028-6034); the message shows the argument the author wrote.
		using var workspace = TestWorkspace.CreateCompilable("#DEFINE FAR 8388608\n" + TestScripts.LanguageAndLevel + "LensFlare= 0,FAR,0,0,0,0\n");

		TestCompileResult result = workspace.Compile();

		DiagnosticAssert.SingleError(
			result.Diagnostics,
			8,
			"Coordinate 2 of LensFlare= is out of range: FAR divided by 256 must fit -32768 to 32767, so the coordinate lies between -8388863 and 8388607"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("#DEFINE X 5+", "The #DEFINE value [5+] is invalid: 5+ has an operator with no value after it; write 5")]
	[DataRow("#DEFINE X", "Wrong number of arguments for '#DEFINE': expected 2, got 1")]
	public void UseOfAFailedDefine_NamesTheDefineLine(string defineLine, string defineMessage)
	{
		// VB6 reads a define value through ConvertiStringaNumerica (Module1.bas:13683); the port rejects the value, and each use of the
		// name points at the failed line rather than calling the name unknown.
		using var workspace = TestWorkspace.CreateCompilable(defineLine + "\n" + TestScripts.LanguageAndLevel + "LevelFarView= X\nUVRotate= X\n");

		TestCompileResult result = workspace.Compile();

		string[] expected =
		[
			defineMessage,
			"Argument 1 of LevelFarView= is invalid: X names the #DEFINE of line 1, which gives no value; fix that line",
			"Argument 1 of UVRotate= is invalid: X names the #DEFINE of line 1, which gives no value; fix that line",
		];

		Assert.AreSequenceEqual(expected, result.Diagnostics.Where(entry => entry.IsError).Select(entry => entry.Message), result.Log);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("ResidentCut= 5,1", "Argument 1 of ResidentCut= is out of range: 5 lies outside 1 to 4, the values a cutscene number holds")]
	[DataRow("ResidentCut= 1,-1", "Argument 2 of ResidentCut= is out of range: -1 lies outside 0 to 255, the values a room number holds")]
	public void ResidentCutArguments_AreNamedAsTheHelpNamesThem(string commandLine, string expectedMessage)
	{
		// The help names the arguments NumberOfCutscene and RoomNumber (help_old_script_command.txt:553-566); VB6 checks the first for
		// 1 to 4 (Module1.bas:5907-5911).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + commandLine + "\n");

		TestCompileResult result = workspace.Compile();

		Assert.AreEqual(expectedMessage, result.Diagnostics.Single(entry => entry.IsError).Message);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Mirror= 1", "Wrong number of arguments for 'Mirror=': expected 2, got 1")]
	[DataRow("Layer2= 1,2,3", "Wrong number of arguments for 'Layer2=': expected 4, got 3")]
	[DataRow("ResetHUB= 1,2", "Wrong number of arguments for 'ResetHUB=': expected 1, got 2")]
	[DataRow("Horizon= ENABLED,1", "Wrong number of arguments for 'Horizon=': expected 1, got 2")]
	public void ClassicArgumentCount_NamesTheExpectedAndTheGivenCount(string commandLine, string expectedMessage)
	{
		// VB6 rejects a wrong argument count before it reads an argument (Module1.bas:6054-6058 for Mirror=); the message names the count
		// expected and the count given.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + commandLine + "\n");

		TestCompileResult result = workspace.Compile();

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);
		Assert.AreEqual((expectedMessage, 7), (diagnostic.Message, diagnostic.LineNumber));
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Name= Test Level\nLevel= ,0\n", "'Level=' gives an empty level path; write the path of the level file before the comma", 5)]
	[DataRow("Name=\nLevel= data\\test,0\n", "'Name=' gives an empty level name; write the name of the level", 4)]
	[DataRow("Name= Test Level\n", "Missing 'Level=' command in [Level] section", 3)]
	[DataRow("Level= data\\test,0\n", "Missing 'Name=' command in [Level] section", 3)]
	public void EmptyLevelOrNameValue_IsReportedAsEmptyNotAsMissing(string lines, string expectedMessage, int expectedLine)
	{
		// VB6 fails a [Level] without Level= (Module1.bas:6430-6434), and the port also fails one without Name= (PD-05); a line with an
		// empty value is there, so the message says it is empty.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Level]\n" + lines + TestScripts.LoadCamera + "\n");

		TestCompileResult result = workspace.Compile();

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);
		Assert.AreEqual((expectedMessage, expectedLine), (diagnostic.Message, diagnostic.LineNumber));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LoadErrorOfAnIncludedFile_IsGeneralAndNamesTheFile()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "#INCLUDE \"Inc.txt\"\n");
		workspace.WithTextFile(Path.Combine("script", "Inc.txt"), "Fog= 1,2,3\n");

		TestCompileResult result = workspace.WithFileLocked(Path.Combine("script", "Inc.txt"), () => workspace.Compile());

		// The message ends in the operating system's message, which the error keeps in its exception.
		string includePath = workspace.PathOf(Path.Combine("script", "Inc.txt"));
		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);

		Assert.IsInstanceOfType<IOException>(diagnostic.Exception);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: null,
			message: $"Cannot read file {includePath}: {diagnostic.Exception.Reason()}",
			file: includePath
		);

		LogEntry entry = result.Entries.Single(logEntry => logEntry.Severity is LogSeverity.Error);
		Assert.AreEqual($"ERROR: General: {diagnostic.Message}", entry.FormattedMessage);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NegativeNumbersInAMessage_AreWrittenWithAnAsciiMinus_UnderACultureWithAnotherMinusSign()
	{
		// Swedish writes the minus sign as U+2212, so a number a message formats without the invariant culture would not read as -5.
		var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
		culture.NumberFormat.NegativeSign = "−";

		Assert.AreNotEqual("-5", (-5).ToString(culture), "The culture of the test must write another minus sign.");

		CultureInfo previous = CultureInfo.CurrentCulture;
		CompilerDiagnostic[] errors;

		try
		{
			CultureInfo.CurrentCulture = culture;

			using var classic = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "Layer1= 1,2,300,4\nLayer2= &HFFFF,2,3,4\n");
			using var options = TestWorkspace.CreateCompilable(TestWorkspace.ScriptWithOptionsCommand("ImportFile= -40000, a.bin, 0, 0"));

			errors = [.. classic.Compile().Diagnostics.Concat(options.Compile().Diagnostics).Where(entry => entry.IsError)];
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}

		Assert.AreSequenceEqual(
			new[]
			{
				"Argument 3 of Layer1= is out of range: 300 lies outside -128 to 255, the values its byte holds",
				"Argument 1 of Layer2= is invalid: VB reads &HFFFF as -1; write $FFFF for 65535, or -1 for the value VB reads",
				"The ImportFile= ID -40000 lies outside -32768 to 65535, the values its word holds",
			},

			errors.Select(error => error.Message).ToArray()
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void VerboseLine_WritesANegativeNumberWithAnAsciiMinus_UnderACultureWithAnotherMinusSign()
	{
		var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
		culture.NumberFormat.NegativeSign = "−";

		Assert.AreNotEqual("-5", (-5).ToString(culture), "The culture of the test must write another minus sign.");

		var entries = new List<LogEntry>();
		var log = new CompilerLog(conciseLogging: false, entries.Add);

		CultureInfo previous = CultureInfo.CurrentCulture;

		try
		{
			CultureInfo.CurrentCulture = culture;
			log.LogVerbose($"Value {-5}, half {-0.5}");
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}

		Assert.AreEqual("Value -5, half -0.5", entries.Single().Message);
	}
}
