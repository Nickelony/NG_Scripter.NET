using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Plugins;

/// <summary>
/// A plugin constant takes the ID of its plugin's <c>Plugin=</c> line and resolves right after the TRNG constants.
/// </summary>
[TestClass]
[TestCategory("IO")]
public sealed class PluginConstantTests
{
	private const string Language = "[Strings]\nTest Level\n\n[ExtraNG]\n100: Plugin_X\n101: Plugin_Y\n";

	private static string ScriptWith(string options, string levelLines) =>
		TestScripts.Language + "[Options]\n" + options + TestScripts.Level + levelLines;

	private static NgCommand FirstLevelCommand(AnalysisResult analysis)
	{
		Assert.IsNotNull(analysis.ScriptModel);
		return analysis.ScriptModel.NgModel.LevelCommands[0].Commands[0];
	}

	[TestMethod]
	public void PluginConstantInCustomize_CarriesTheIdOfThePluginLineInItsHighWord()
	{
		// VB6 ORs the plugin ID of the first argument of Customize= and Parameters= and of the second of AssignSlot= into the high word
		// (Module1.bas:7443-7455), and the constant takes the ID from its plugin's Plugin= line.
		using var workspace = TestWorkspace.CreateCompilable(ScriptWith("Plugin= 2,Plugin_X,IGNORE\n", "Customize= PLUGIN_X_CONST,1,2\n"), Language);

		workspace.WithPluginFile("Plugin_X.script", "<START_CONSTANTS>\r\nPLUGIN_X_CONST:$40\r\n<END>\r\n");

		AnalysisResult analysis = workspace.Analyze();

		Assert.IsTrue(analysis.Success, analysis.Log);

		NgCommand customize = FirstLevelCommand(analysis);

		Assert.AreEqual(0x0002_0040, customize.Arguments[0].Int32Value);
		Assert.AreSequenceEqual(new short[] { 0x0040, 0x0002 }, customize.CompiledWords.Skip(1).Take(2));
	}

	[TestMethod]
	public void PluginLineBelowTheLevel_GivesTheConstantItsId()
	{
		// VB6 builds its plugin table from every Plugin= line of script.txt before it compiles (Module1.bas:3868, 15024-15104).
		using var workspace = TestWorkspace.CreateCompilable(
			ScriptWith(string.Empty, "Customize= PLUGIN_X_CONST,1,2\n") + "[Options]\nPlugin= 3,Plugin_X,IGNORE\n",
			Language
		);

		workspace.WithPluginFile("Plugin_X.script", "<START_CONSTANTS>\r\nPLUGIN_X_CONST:$40\r\n<END>\r\n");

		AnalysisResult analysis = workspace.Analyze();

		Assert.IsTrue(analysis.Success, analysis.Log);
		Assert.AreEqual(0x0003_0040, FirstLevelCommand(analysis).Arguments[0].Int32Value);
	}

	[TestMethod]
	public void PluginConstant_ComesBeforeASlotName()
	{
		// VB6 merges the plugin constants into its constant table (Module1.bas:4248-4269), which it looks up before the slot names
		// (:10116-10127), so a plugin constant named LARA is the plugin's value, not slot 0.
		using var workspace = TestWorkspace.CreateCompilable(ScriptWith("Plugin= 2,Plugin_X,IGNORE\n", "Customize= LARA,1,2\n"), Language);
		workspace.WithStockObjectsH();
		workspace.WithPluginFile("Plugin_X.script", "<START_CONSTANTS>\r\nLARA:$40\r\n<END>\r\n");

		AnalysisResult analysis = workspace.Analyze();

		Assert.IsTrue(analysis.Success, analysis.Log);
		Assert.AreEqual(0x0002_0040, FirstLevelCommand(analysis).Arguments[0].Int32Value);
	}

	[TestMethod]
	[DataRow("PLUGIN_X_CONST")]
	[DataRow("plugin_x_const")]
	public void PluginConstantWithoutAPluginLine_FailsNamingTheLineToAdd(string written)
	{
		// PD-61: NG Center refuses to compile while an installed plugin has no Plugin= line (Principale.frm:4389-4402).
		using var workspace = TestWorkspace.CreateCompilable(ScriptWith(string.Empty, $"Customize= {written},1,2\n"), Language);
		workspace.WithPluginFile("Plugin_X.script", "<START_CONSTANTS>\r\nPLUGIN_X_CONST:$40\r\n<END>\r\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		CompilerDiagnostic diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.IsError);

		Assert.AreEqual(
			"Argument 1 of Customize= is invalid: PLUGIN_X_CONST is a constant of the plugin Plugin_X, which no Plugin= line declares; add a Plugin= line for Plugin_X to [Options]",
			diagnostic.Message
		);

		Assert.AreEqual(8, diagnostic.LineNumber);
	}

	[TestMethod]
	[DataRow("CUST_SETUP:1", "CUST_SETUP:2", "Plugin_X (1) and Plugin_Y (2)")]
	[DataRow("CUST_SETUP:1", "CUST_SETUP:1", "Plugin_X (1) and Plugin_Y (1)")]
	public void NameTwoPluginsDefine_FailsWhereTheScriptUsesIt(string first, string second, string sources)
	{
		// PD-62: VB6 offers at startup to rename one copy CUST_SETUP_2 and rewrite its script (Module1.bas:4276-4318). With one value the
		// two plugins still give the name two plugin IDs.
		using var workspace = TestWorkspace.CreateCompilable(
			ScriptWith("Plugin= 2,Plugin_X,IGNORE\nPlugin= 3,Plugin_Y,IGNORE\n", "Customize= CUST_SETUP,1,2\n"),
			Language
		);

		workspace.WithPluginFile("Plugin_X.script", $"<START_CONSTANTS>\r\n{first}\r\n<END>\r\n");
		workspace.WithPluginFile("Plugin_Y.script", $"<START_CONSTANTS>\r\n{second}\r\n<END>\r\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		Assert.AreEqual(
			$"Argument 1 of Customize= is invalid: CUST_SETUP is defined by the plugins {sources}, so it does not say which plugin it means; rename the constant in all but one of the plugin scripts",
			result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message
		);
	}

	[TestMethod]
	public void NameTwoPluginsDefine_CompilesWhileTheScriptDoesNotUseIt()
	{
		using var workspace = TestWorkspace.CreateCompilable(ScriptWith("Plugin= 2,Plugin_X,IGNORE\n", string.Empty), Language);

		workspace.WithPluginFile("Plugin_X.script", "<START_CONSTANTS>\r\nCUST_SETUP:1\r\n<END>\r\n");
		workspace.WithPluginFile("Plugin_Y.script", "<START_CONSTANTS>\r\nCUST_SETUP:2\r\n<END>\r\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
	}

	[TestMethod]
	public void PluginConstantThatHidesATRNGConstant_WarnsAtLoadAndTheTRNGValueWins()
	{
		// PD-62: VB6 offers to rename the plugin's copy at startup (Module1.bas:4276-4318); the TRNG constant keeps its value.
		using var workspace = TestWorkspace.CreateCompilable(
			ScriptWith("Plugin= 2,Plugin_X,IGNORE\n", "Customize= CUST_DISABLE_SCREAMING_HEAD\n"),
			Language
		);

		workspace.WithPluginFile("Plugin_X.script", "<START_CONSTANTS>\r\nCUST_DISABLE_SCREAMING_HEAD:$40\r\n<END>\r\n");

		AnalysisResult analysis = workspace.Analyze();

		Assert.IsTrue(analysis.Success, analysis.Log);

		DiagnosticAssert.HasWarning(
			diagnostics: analysis.Diagnostics,
			line: 2,
			message: "The plugin constant CUST_DISABLE_SCREAMING_HEAD is 64, but the TRNG constant CUST_DISABLE_SCREAMING_HEAD is 1, which a script that names it gets; rename the constant in the plugin script to use the plugin's value",
			file: workspace.PathOf("Plugin_X.script")
		);

		Assert.AreEqual(1, FirstLevelCommand(analysis).Arguments[0].Int32Value);
	}

	[TestMethod]
	public void PluginWithoutAPluginLine_LeavesTheInputsAsWritten()
	{
		// PD-49: NG Center's startup inserts a Plugin= line into script.txt and the plugin's name into the language files
		// (Module1.bas:15105-15274) before it compiles; the port compiles the files as they are.
		using var workspace = TestWorkspace.CreateCompilable();
		workspace.WithPluginFile("Plugin_X.script", "<START_CONSTANTS>\r\nPLUGIN_X_CONST:$40\r\n<END>\r\n");

		byte[] script = File.ReadAllBytes(workspace.PathOf(Path.Combine("script", "Script.txt")));
		byte[] language = File.ReadAllBytes(workspace.PathOf(Path.Combine("script", "English.txt")));

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		Assert.AreSequenceEqual(script, File.ReadAllBytes(workspace.PathOf(Path.Combine("script", "Script.txt"))));
		Assert.AreSequenceEqual(language, File.ReadAllBytes(workspace.PathOf(Path.Combine("script", "English.txt"))));
	}
}
