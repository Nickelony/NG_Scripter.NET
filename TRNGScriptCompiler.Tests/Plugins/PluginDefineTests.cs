using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Plugins;

/// <summary>
/// A <c>#DEFINE @plugin</c> line names the plugin of the exported triggers below it, which take the ID
/// of its <c>Plugin=</c> line.
/// </summary>
[TestClass]
[TestCategory("IO")]
public sealed class PluginDefineTests
{
	private const string Language = "[Strings]\nTest Level\nLevel Two\n\n[ExtraNG]\n100: Plugin_A\n";
	private const string Header = TestScripts.Language + "[Options]\nPlugin= $5,Plugin_A,IGNORE\n";

	private static string Level(string name, int number, string lines) =>
		$"[Level]\nName= {name}\nLevel= data\\level{number},{number}\nLoadCamera= 0,0,0,0,0,0,0\n" + lines;

	// The high word of the first value of each triplet holds the plugin ID (Module1.bas:7558-7574).
	private static int[] TriggerPluginIds(NgCommandGroup group) =>
		[.. group.Commands.Select(command => (command.Arguments[1].Int32Value >> 16) & 0xFFFF)];

	[TestMethod]
	public void DefineBlock_AppliesInSourceOrderAndHoldsInTheLevelsAfterIt()
	{
		// VB6 applies each #define @plugin among the NG commands of its section (Module1.bas:8457) and empties the table only before the
		// first level (:8420), so the block holds in the next level until a clear.
		using var workspace = TestWorkspace.CreateCompilable(
			Header
				+ Level("Test Level", 1, "TriggerGroup= 1,$70001,406,1\n#define @Plugin_A 7\nTriggerGroup= 2,$70002,406,2\n")
				+ Level("Level Two", 2, "TriggerGroup= 1,$70003,406,3\n#define @plugins clear\nTriggerGroup= 2,$70004,406,4\n"),

			Language
		);

		AnalysisResult analysis = workspace.Analyze();

		Assert.IsTrue(analysis.Success, analysis.Log);

		Assert.IsNotNull(analysis.ScriptModel);

		IReadOnlyList<NgCommandGroup> levels = analysis.ScriptModel.NgModel.LevelCommands;

		Assert.AreSequenceEqual(new[] { 7, 5 }, TriggerPluginIds(levels[0]), "A trigger above the define keeps its ID.");
		Assert.AreSequenceEqual(new[] { 5, 7 }, TriggerPluginIds(levels[1]), "The block holds until the clear.");
	}

	[TestMethod]
	public void PluginLineBelowTheLevelsOrInAnInclude_TranslatesTheTrigger()
	{
		// VB6 reads every Plugin= line of script.txt, wherever it stands and whether or not the plugin is installed (Module1.bas:15024-15104);
		// the port also reads the lines of included files, which VB6 misses (PD-87).
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language
				+ Level("Test Level", 1, "#define @Plugin_A 7\n#define @Plugin_B 9\nTriggerGroup= 1,$70001,406,1,$90002,406,2\n")
				+ "[Options]\nPlugin= 5,Plugin_A,IGNORE\n#INCLUDE \"plugins.txt\"\n",

			Language + "101: Plugin_B\n"
		);

		workspace.WithTextFile(Path.Combine("script", "plugins.txt"), "Plugin= $6,Plugin_B,IGNORE\n");

		AnalysisResult analysis = workspace.Analyze();

		Assert.IsTrue(analysis.Success, analysis.Log);

		Assert.IsNotNull(analysis.ScriptModel);

		NgCommand trigger = analysis.ScriptModel.NgModel.LevelCommands[0].Commands.Single();
		IReadOnlyList<NgArgumentValue>? array = trigger.Arguments[4].ArrayValue;

		Assert.AreEqual(0x0005_0001, trigger.Arguments[1].Int32Value);
		Assert.IsNotNull(array);
		Assert.AreEqual(0x0006_0002, array[0].Int32Value);
	}

	[TestMethod]
	[DataRow("[Options]")]
	[DataRow("[PCExtensions]")]
	public void DefineOutsideALevelOrTitle_Fails(string section)
	{
		// VB6 reads the line there as the unknown command #define, which fails (Module1.bas:5285-5289, 5433-5440, 5524-5535).
		using var workspace = TestWorkspace.CreateCompilable(
			Header + $"{section}\n#define @Plugin_A 7\n" + Level("Test Level", 1, string.Empty),
			Language
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		CompilerDiagnostic diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.IsError);

		Assert.AreEqual(
			$"A #DEFINE @plugin line names the plugin of the exported triggers that follow it in a [Level] or [Title] section, and has no effect in {section}; move it into the section of its triggers",
			diagnostic.Message
		);

		Assert.AreEqual(6, diagnostic.LineNumber);
	}

	[TestMethod]
	public void OnePluginDeclaredUnderTwoIds_Fails()
	{
		// PD-57: VB6 writes both lines, and its trigger table takes the first ID (Module1.bas:6993-6995).
		using var workspace = TestWorkspace.CreateCompilable(
			Header + "Plugin= 6,Plugin_A,IGNORE\n" + Level("Test Level", 1, "#define @Plugin_A 7\nTriggerGroup= 1,$70001,406,1\n"),
			Language
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		CompilerDiagnostic diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.IsError);

		Assert.AreEqual(
			"Plugin= declares Plugin_A with ID 6, but line 4 declares it with ID 5, and a plugin takes one ID; keep one of the two lines",
			diagnostic.Message
		);

		Assert.AreEqual(5, diagnostic.LineNumber);
	}

	[TestMethod]
	public void DuplicatePluginId_MarksTheIdWhenItRepeatsPartOfThePluginName()
	{
		// The ID 42 is also the end of the name @Plugin_42, so a search for the text 42 would find the name first.
		using var workspace = TestWorkspace.CreateCompilable(
			Header + Level("Test Level", 1, "#define @Plugin_A 42\n#define @Plugin_42 42\nTriggerGroup= 1,$70001,406,1\n"),
			Language
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic error = DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 10,
			message: "The plugin ID 42 is already defined for another plugin; add a [#DEFINE @plugins clear] line to clear the previous definitions",
			file: workspace.ScriptFile
		);

		// "#define @Plugin_42 " is 19 characters long.
		Assert.AreEqual((19, 2), (error.Column, error.Length));
	}

	[TestMethod]
	[DataRow("FOO")]
	[DataRow("MY_ID")]
	public void PluginLineIdThatIsNoNumber_IsReportedOnce(string id)
	{
		// PD-87: VB6 reads the ID with GetCampoNumero and skips a line it cannot read (Module1.bas:15081-15083); the port fails once,
		// whether or not the name resolves.
		using var workspace = TestWorkspace.CreateCompilable(
			"#DEFINE MY_ID 6\n" + Header + $"Plugin= {id},Plugin_B,IGNORE\n" + Level("Test Level", 1, string.Empty),
			Language
		);

		TestCompileResult result = workspace.Compile();

		Assert.AreEqual(
			$"The plugin ID '{id}' of Plugin= is not a number; write the ID as a decimal or $ number",
			result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message,
			result.Log
		);
	}
}
