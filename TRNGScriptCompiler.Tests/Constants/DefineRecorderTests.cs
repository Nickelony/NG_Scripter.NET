using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Constants;

[TestClass]
[TestCategory("Fast")]
public sealed class DefineRecorderTests
{
	private static readonly string s_file = Path.GetFullPath("Script.txt");

	private sealed record Fixture(DefineRecorder Recorder, DefineRegistry Defines, DiagnosticSink Diagnostics, CompilerLog Logger);

	/// <summary>Scans <c>Script.txt</c> with one line, the first of which is line 1.</summary>
	private static void Record(Fixture fixture, string text, int lineNumber) =>
		fixture.Recorder.Scan(s_file, [.. Enumerable.Repeat(string.Empty, lineNumber - 1), text]);

	private static Fixture CreateFixture()
	{
		var run = new CompilationRun(conciseLogging: false);

		var evaluator = new ExpressionEvaluator(
			(name, _) =>
				name.ToUpperInvariant() switch
				{
					"ALPHA" => new ConstantResolutionResult(Success: true, PluginId: 0, Value: 10, Kind: ConstantKind.Constant),
					_ => default,
				}
		);

		return new Fixture(Recorder: new DefineRecorder(run, evaluator), Defines: run.Defines, Diagnostics: run.Diagnostics, Logger: run.Logger);
	}

	[TestMethod]
	public void Scan_NonDefineLine_RecordsNothing()
	{
		Fixture fixture = CreateFixture();

		Record(fixture, "LoadCamera= 1,2,3,4,5,6,7", 3);

		DiagnosticAssert.NoDiagnostics(fixture.Diagnostics.Recorded);
	}

	[TestMethod]
	public void Scan_StoresDecimalAndHexValues_CaseInsensitively()
	{
		Fixture fixture = CreateFixture();

		Record(fixture, "#DEFINE LOCAL_CONST 10", 1);

		Record(fixture, "#DEFINE HEX_CONST $20", 2);

		Assert.IsTrue(fixture.Defines.TryGet(s_file, "local_const", out DefineRecord? local));
		Assert.AreEqual(10, local.Value);
		Assert.IsTrue(fixture.Defines.TryGet(s_file, "HEX_CONST", out DefineRecord? hex));
		Assert.AreEqual(0x20, hex.Value);

		Assert.IsFalse(fixture.Defines.TryGet(s_file, "MISSING", out _));
		DiagnosticAssert.NoDiagnostics(fixture.Diagnostics.Recorded);
	}

	[TestMethod]
	public void TwoFilesOfOneNameInDifferentFolders_KeepTheirOwnDefines()
	{
		// VB6 keys a define on the file name after its last '\' or ':' (Module1.bas:10085-10091); the port keys it on the full path, so
		// two include files of one name are two scopes.
		Fixture fixture = CreateFixture();

		string first = Path.GetFullPath(Path.Combine("a", "defs.txt"));
		string second = Path.GetFullPath(Path.Combine("b", "defs.txt"));

		fixture.Recorder.Scan(first, ["#DEFINE SPEED 1"]);
		fixture.Recorder.Scan(second, ["#DEFINE SPEED 2"]);

		// A define in another file is no repeat.
		DiagnosticAssert.NoDiagnostics(fixture.Diagnostics.Recorded);

		Assert.IsTrue(fixture.Defines.TryGet(first, "SPEED", out DefineRecord? firstDefine));
		Assert.IsTrue(fixture.Defines.TryGet(second, "SPEED", out DefineRecord? secondDefine));
		Assert.AreEqual((1, 2), (firstDefine.Value, secondDefine.Value));
		Assert.IsFalse(fixture.Defines.TryGet(s_file, "SPEED", out _), "A define is visible only in the file that declares it.");
	}

	[TestMethod]
	public void Scan_WrongArgumentCount_Fails()
	{
		Fixture fixture = CreateFixture();

		Record(fixture, "#DEFINE ONLY_TWO", 4);

		DiagnosticAssert.SingleError(
			diagnostics: fixture.Diagnostics.Recorded,
			line: 4,
			message: "Wrong number of arguments for '#DEFINE': expected 2, got 1",
			file: s_file
		);
	}

	[TestMethod]
	public void Scan_UnresolvableValue_Fails()
	{
		Fixture fixture = CreateFixture();

		Record(fixture, "#DEFINE BAD_NAME NOPE+1", 7);

		DiagnosticAssert.SingleError(
			diagnostics: fixture.Diagnostics.Recorded,
			line: 7,
			message: "The #DEFINE value [NOPE+1] is invalid: NOPE is neither a number nor a known name",
			file: s_file
		);
	}

	[TestMethod]
	public void Scan_InvalidValueThatRepeatsTheName_MarksTheValue()
	{
		Fixture fixture = CreateFixture();

		Record(fixture, "#DEFINE NOPE NOPE", 2);

		CompilerDiagnostic error = DiagnosticAssert.SingleError(
			diagnostics: fixture.Diagnostics.Recorded,
			line: 2,
			message: "The #DEFINE value [NOPE] is invalid: NOPE is neither a number nor a known name",
			file: s_file
		);

		Assert.AreEqual((13, 4), (error.Column, error.Length));
	}

	[TestMethod]
	public void Scan_RedefinitionOfANameThatIsPartOfTheDirective_MarksTheName()
	{
		Fixture fixture = CreateFixture();

		Record(fixture, "#DEFINE DEF 10", 1);
		Record(fixture, "#DEFINE DEF 20", 2);

		CompilerDiagnostic error = DiagnosticAssert.SingleError(
			diagnostics: fixture.Diagnostics.Recorded,
			line: 2,
			message: "#DEFINE DEF gives a value different from line 1, and a define holds one value; keep one of the two lines",
			file: s_file
		);

		Assert.AreEqual((8, 3), (error.Column, error.Length));
	}

	[TestMethod]
	public void Scan_RedefinitionWithDifferentValue_Fails()
	{
		// PD-31: VB6 warns and keeps the first value (Module1.bas:13691-13698), so the script does not say which value it means.
		Fixture fixture = CreateFixture();

		Record(fixture, "#DEFINE DUP 10", 1);
		Record(fixture, "#DEFINE DUP 20", 2);

		DiagnosticAssert.SingleError(
			diagnostics: fixture.Diagnostics.Recorded,
			line: 2,
			message: "#DEFINE DUP gives a value different from line 1, and a define holds one value; keep one of the two lines",
			file: s_file
		);
	}

	[TestMethod]
	public void Scan_RedefinitionWithSameValue_Warns()
	{
		Fixture fixture = CreateFixture();

		Record(fixture, "#DEFINE SAME 5", 1);
		Record(fixture, "#DEFINE SAME 5", 2);

		DiagnosticAssert.HasWarning(
			diagnostics: fixture.Diagnostics.Recorded,
			line: 2,
			message: "#DEFINE SAME repeats line 1 with the same value; remove one of the two lines",
			file: s_file
		);

		Assert.HasCount(1, fixture.Diagnostics.Recorded, "An identical repeat only warns.");
	}

	[TestMethod]
	public void DefineWordWithExtraLetters_IsNoDefine()
	{
		// VB6 compares the whole first word with #DEFINE (Module1.bas:13664), so #DEFINEX FOO 1 gives FOO no value.
		Fixture fixture = CreateFixture();

		Record(fixture, "#DEFINEX FOO 1", 1);
		DiagnosticAssert.NoDiagnostics(fixture.Diagnostics.Recorded);
	}

	[TestMethod]
	public void PluginDefineLine_IsLeftToTheNgCompile()
	{
		// VB6 reads a #define @plugin by its place among the NG commands of its section (Module1.bas:5283-5289, 8457), so the define
		// pre-scan records nothing for it.
		Fixture fixture = CreateFixture();

		Record(fixture, "#DEFINE @Plugin_A 42", 1);

		Assert.IsFalse(fixture.Defines.TryGet(s_file, "@Plugin_A", out _));
		DiagnosticAssert.NoDiagnostics(fixture.Diagnostics.Recorded);
	}
}
