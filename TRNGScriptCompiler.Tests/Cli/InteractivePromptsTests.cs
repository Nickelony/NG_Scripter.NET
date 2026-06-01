using System.Text;
using TRNGScriptCompiler.Cli;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Cli;

[TestClass]
public sealed class InteractivePromptsTests
{
	private const string EnginePrompt = "Enter engine folder path: ";
	private const string ScriptPrompt = "Enter script folder path (press Enter to autodetect): ";
	private const string PluginPrompt = "Enter plugin folder path (press Enter for NG_Center, or the engine folder without it): ";

	[TestMethod]
	[TestCategory("IO")]
	public void QuotedPaths_AreReadWithoutTheirQuotes()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		string plugins = workspace.PathOf("Plugins");
		var output = new StringBuilder();
		string answers = $"\"{workspace.RootPath}\"\n \"{workspace.ScriptDirectory}\" \n\"{plugins}\"\n";
		var prompts = new InteractivePrompts(new StringReader(answers), Record(output));

		TRNGCompilerOptions? options = prompts.Ask();

		Assert.IsNotNull(options);
		Assert.AreEqual(workspace.RootPath, options.EngineDirectory);
		Assert.AreEqual(workspace.ScriptDirectory, options.ScriptDirectory);
		Assert.AreEqual(plugins, options.PluginDirectory);
		Assert.AreEqual(EnginePrompt + ScriptPrompt + PluginPrompt, output.ToString());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void EmptyScriptAnswer_AutodetectsTheScriptFolderAndSaysSo()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		var output = new StringBuilder();
		var prompts = new InteractivePrompts(new StringReader(workspace.RootPath + "\n\n\n"), Record(output));

		TRNGCompilerOptions? options = prompts.Ask();

		Assert.IsNotNull(options);
		Assert.AreEqual(workspace.ScriptDirectory, options.ScriptDirectory);

		string autodetected = $"  Autodetect -> {workspace.ScriptDirectory}{Environment.NewLine}";

		Assert.AreEqual(EnginePrompt + ScriptPrompt + autodetected + PluginPrompt, output.ToString());
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("")]
	[DataRow("\"\"")]
	[DataRow(" \" \" ")]
	[DataRow(null)]
	public void EmptyEngineAnswer_GivesNoOptionsAndAsksNothingMore(string? engineAnswer)
	{
		var output = new StringBuilder();
		string answers = engineAnswer is null ? string.Empty : engineAnswer + "\n";
		var prompts = new InteractivePrompts(new StringReader(answers), Record(output));

		Assert.IsNull(prompts.Ask());
		Assert.AreEqual(EnginePrompt, output.ToString());
	}

	private static Action<string, ConsoleColor?> Record(StringBuilder output) => (text, _) => output.Append(text);
}
