using System.Reflection;
using System.Text;

namespace TRNGScriptCompiler.Tests.Support;

/// <summary>
/// Builds the workspace of the command coverage matrices: a script with a title and a level that compile,
/// in which one command line replaces its command in the baseline lines of its section, or joins them.
/// </summary>
/// <remarks>
/// The language file holds the strings that the matrix samples name, and the second language file,
/// the plugin and the import file are there for the samples that need them.
/// </remarks>
public static class CoverageWorkspace
{
	/// <summary>Gets the bytes of the file that the <c>ImportFile=</c> sample imports.</summary>
	internal static byte[] ImportFileData { get; } = Encoding.ASCII.GetBytes("matrix-import-data\r\n");

	/// <summary>
	/// Renders dynamic data rows as <c>Method(scenarioName)</c> instead of the default object-array dump.
	/// </summary>
	public static string GetScenarioDisplayName(MethodInfo methodInfo, object[] values) => $"{methodInfo.Name}({values[0]})";

	/// <summary>Returns the command of a script line with its <c>=</c>, or the whole line when it has none.</summary>
	/// <param name="line">The script line.</param>
	/// <returns>The command name.</returns>
	public static string ExtractCommandName(string line)
	{
		int separatorIndex = line.IndexOf('=');
		return separatorIndex >= 0 ? line[..(separatorIndex + 1)] : line;
	}

	/// <summary>
	/// Compiles the coverage script with one command line, asserts that the compile succeeds and writes both outputs,
	/// and returns the bytes of its <c>Script.dat</c>.
	/// </summary>
	/// <param name="scenarioName">The name of the scenario, for a failure message.</param>
	/// <param name="section">The section that takes the command line.</param>
	/// <param name="commandLine">The command line.</param>
	/// <returns>The bytes of <c>Script.dat</c>.</returns>
	public static byte[] CompileScriptDat(string scenarioName, ScenarioSection section, string commandLine)
	{
		using TestWorkspace workspace = Create(section, commandLine);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, $"Scenario '{scenarioName}' failed for line '{commandLine}'.{Environment.NewLine}{result.Log}");

		Assert.IsTrue(
			File.Exists(workspace.PathOf("Script.dat")),
			$"Scenario '{scenarioName}' did not produce Script.dat.{Environment.NewLine}{result.Log}"
		);

		Assert.IsTrue(
			File.Exists(workspace.PathOf("English.dat")),
			$"Scenario '{scenarioName}' did not produce English.dat.{Environment.NewLine}{result.Log}"
		);

		return File.ReadAllBytes(workspace.PathOf("Script.dat"));
	}

	private static TestWorkspace Create(ScenarioSection section, string commandLine)
	{
		var workspace = TestWorkspace.Create(BuildScript(section, commandLine));
		workspace.WithLanguageFile("English.txt", BuildEnglishLanguageFile());

		// Only the Language row lists this file, as language 1.
		workspace.WithLanguageFile("French.txt", BuildEnglishLanguageFile());

		workspace.WithObjectsH();
		workspace.WithPluginFile("PluginMatrix.script", "<START_CONSTANTS>\r\nPLUGIN_MATRIX_CONST:$40\r\n<END>\r\n");
		workspace.WithScriptBinaryFile(Path.Combine("extras", "matrix009.bin"), ImportFileData);

		return workspace;
	}

	private static string BuildScript(ScenarioSection section, string commandLine)
	{
		var languageLines = new List<string> { "File= 0,English.txt" };
		var optionLines = new List<string> { "Title= ENABLED", "Security= 0" };
		var psxExtensionLines = new List<string> { "Level= .PSX", "FMV= .FMV", "Cut= .CUT" };
		var pcExtensionLines = new List<string> { "Level= .TR4", "FMV= .BIK", "Cut= .TR4" };
		var titleLines = new List<string> { "Level= data\\title,0", TestScripts.LoadCamera };
		var levelLines = new List<string> { "Name= Test Level", "Level= data\\test,0", TestScripts.LoadCamera };

		switch (section)
		{
			// A File= line is numbered, so a sample adds the next one.
			case ScenarioSection.Language:
				languageLines.Add(commandLine);
				break;

			case ScenarioSection.Options:
				UpsertCommandLine(optionLines, commandLine);
				break;

			case ScenarioSection.PsxExtensions:
				UpsertCommandLine(psxExtensionLines, commandLine);
				break;

			case ScenarioSection.PcExtensions:
				UpsertCommandLine(pcExtensionLines, commandLine);
				break;

			case ScenarioSection.Title:
				UpsertCommandLine(titleLines, commandLine);
				break;

			case ScenarioSection.Level:
				UpsertCommandLine(levelLines, commandLine);
				break;
		}

		var builder = new StringBuilder();

		AppendSection(builder, "Language", languageLines);
		AppendSection(builder, "Options", optionLines);
		AppendSection(builder, "PSXExtensions", psxExtensionLines);
		AppendSection(builder, "PCExtensions", pcExtensionLines);
		AppendSection(builder, "Title", titleLines);
		AppendSection(builder, "Level", levelLines);

		return builder.ToString();
	}

	private static void AppendSection(StringBuilder builder, string sectionName, IEnumerable<string> lines)
	{
		builder.AppendLine($"[{sectionName}]");

		foreach (string line in lines)
			builder.AppendLine(line);

		builder.AppendLine();
	}

	private static void UpsertCommandLine(List<string> lines, string commandLine)
	{
		string commandName = ExtractCommandName(commandLine);
		int existingIndex = lines.FindIndex(line => string.Equals(ExtractCommandName(line), commandName, StringComparison.OrdinalIgnoreCase));

		if (existingIndex >= 0)
			lines[existingIndex] = commandLine;
		else
			lines.Add(commandLine);
	}

	// VB6 requires a Plugin= name to be an ExtraNG string.
	private static string BuildEnglishLanguageFile()
	{
		return TestWorkspace.LanguageFileWithExtraNg(
			[
				"Test Level",
				"Level Name",
				"Legend Line",
				"Key Label",
				"Puzzle Label",
				"Combo Label",
				"Pickup Label",
				"Examine Label",
				"Damage String",
				"Debug Window",
				"Font String",
				"Diary String",
				"Savegame String",
				"Standby String",
				"Demo One",
				"Demo Two",
			],

			(100, "PluginMatrix")
		);
	}
}
