using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Plugins;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

/// <summary>
/// On a case-sensitive file system, a compile finds every file the script names and replaces the outputs VB6
/// wrote (PD-29).
/// </summary>
[TestClass]
[TestCategory("IO")]
public sealed class CaseSensitivePathTests
{
	private const string Script =
		"[Language]\nFile= 0, ENGLISH.TXT\n[Options]\nImportFile= 1,EXTRAS\\DATA.BIN,FTYPE_USERFILE,IMPORT_TEMPORARY\n#INCLUDE \"SUB\\LEVEL.TXT\"\n";

	private const string Language = "[Strings]\nTest Level\n[ExtraNG]\n100: @NOTE.TXT\n";

	private static readonly byte[] s_import = [0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8];

	[TestMethod]
	public void PathsTheScriptNames_FindTheirFilesInAnotherCasing()
	{
		// VB6 opens each of these through the Windows file functions, which ignore case: the language file (Module1.bas:8887-8891), the
		// include (:5031), the import (:7635) and the @file text (:1854).
		using TestWorkspace workspace = TestWorkspace
			.CreateCaseSensitive()
			.WithScript(Script)
			.WithLanguageFile("English.txt", Language)
			.WithTextFile(@"script\sub\Level.txt", TestScripts.Level)
			.WithTextFile(@"script\Note.txt", "A note")
			.WithBinaryFile(@"extras\Data.bin", s_import);

		// The same script, with every file named as the script writes it.
		using TestWorkspace written = TestWorkspace
			.CreateEmpty()
			.WithScript(Script)
			.WithLanguageFile("ENGLISH.TXT", Language)
			.WithTextFile(@"script\SUB\LEVEL.TXT", TestScripts.Level)
			.WithTextFile(@"script\NOTE.TXT", "A note")
			.WithBinaryFile(@"EXTRAS\DATA.BIN", s_import);

		TestCompileResult result = workspace.Compile();
		TestCompileResult writtenResult = written.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.IsTrue(writtenResult.Success, writtenResult.Log);

		Assert.AreSequenceEqual(written.ReadScriptDat(), workspace.ReadScriptDat(), "Script.dat names each file as the script writes it.");
		Assert.AreSequenceEqual(written.ReadOutput("ENGLISH.dat"), workspace.ReadOutput("ENGLISH.dat"), "The .dat keeps the casing of File=.");
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux)]
	public void EngineFolderNameThatHoldsABackslash_FindsEveryFileTheScriptNames()
	{
		// Linux allows '\' in a folder name, which Windows reads as a separator, so the test runs on Linux alone. A '\' separates folders
		// only in the paths the script writes, so the engine folder keeps its name.
		using var workspace = TestWorkspace.CreateEmpty();

		string engine = Path.Join(workspace.RootPath, @"trle\one");

		WriteFile(engine, "script/Script.txt", Script);
		WriteFile(engine, "script/English.txt", Language);
		WriteFile(engine, "script/sub/Level.txt", TestScripts.Level);
		WriteFile(engine, "script/Note.txt", "A note");

		Directory.CreateDirectory(Path.Join(engine, "extras"));
		File.WriteAllBytes(Path.Join(engine, "extras", "Data.bin"), s_import);

		using TestWorkspace written = TestWorkspace
			.CreateEmpty()
			.WithScript(Script)
			.WithLanguageFile("ENGLISH.TXT", Language)
			.WithTextFile(@"script\SUB\LEVEL.TXT", TestScripts.Level)
			.WithTextFile(@"script\NOTE.TXT", "A note")
			.WithBinaryFile(@"EXTRAS\DATA.BIN", s_import);

		TestCompileResult result = workspace.Compile(
			TRNGCompilerOptions.CreateDefaults(engine, "English") with
			{
				DeterministicSeed = TestWorkspace.DefaultDeterministicSeed,
			}
		);

		TestCompileResult writtenResult = written.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.IsTrue(writtenResult.Success, writtenResult.Log);

		Assert.AreSequenceEqual(written.ReadScriptDat(), File.ReadAllBytes(Path.Join(engine, "Script.dat")));
		Assert.AreSequenceEqual(written.ReadOutput("ENGLISH.dat"), File.ReadAllBytes(Path.Join(engine, "ENGLISH.dat")));
	}

	[TestMethod]
	public void Outputs_ReplaceTheFilesOfAnotherCasing()
	{
		// VB6 writes script.dat (Module1.bas:8958) and the port Script.dat, so a new Script.dat beside the old script.dat would leave the
		// engine folder two scripts on a file system that tells the two apart.
		using TestWorkspace workspace = TestWorkspace
			.CreateCaseSensitive()
			.WithScript(TestScripts.LanguageAndLevel)
			.WithLanguageFile("English.txt", TestWorkspace.CompilableLanguageContent)
			.WithBinaryFile("script.dat", [1, 2, 3])
			.WithBinaryFile("english.dat", [4, 5, 6]);

		using var fresh = TestWorkspace.CreateCompilable();

		TestCompileResult result = workspace.Compile();
		TestCompileResult freshResult = fresh.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.IsTrue(freshResult.Success, freshResult.Log);

		string[] outputs = [.. Directory.GetFiles(workspace.RootPath).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

		Assert.AreSequenceEqual(new[] { "english.dat", "script.dat" }, outputs);
		Assert.AreSequenceEqual(fresh.ReadScriptDat(), workspace.ReadOutput("script.dat"));
		Assert.AreSequenceEqual(fresh.ReadOutput("English.dat"), workspace.ReadOutput("english.dat"));
	}

	[TestMethod]
	public void Outputs_KeepTheNamesOfTheFilesTheyReplace()
	{
		// VB6 deletes the old file before it copies the new one (CopiaFile, MioModulo.bas:1283-1296), so its outputs take the names it
		// composes (Module1.bas:8881, 8958). The port moves each output over the old file of another casing and keeps that file's name, in a
		// folder that ignores case too.
		using TestWorkspace workspace = TestWorkspace
			.CreateCompilable()
			.WithBinaryFile("script.dat", [1, 2, 3])
			.WithBinaryFile("ENGLISH.DAT", [4, 5, 6]);

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		var created = new List<string>();

		compiler.LogEntryProduced += (_, e) =>
		{
			if (e.Entry.Message.StartsWith("Created: ", StringComparison.Ordinal))
				created.Add(e.Entry.Message);
		};

		CompileResult result = compiler.Compile(workspace.CreateOptions());

		Assert.IsTrue(result.Success, result.Log);

		string[] outputs = [.. Directory.GetFiles(workspace.RootPath).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

		Assert.AreSequenceEqual(new[] { "ENGLISH.DAT", "script.dat" }, outputs);
		Assert.AreSequenceEqual(new[] { "ENGLISH.DAT", "script.dat" }, result.PublishedFiles.Select(Path.GetFileName).ToArray());
		Assert.AreSequenceEqual(["Created: ENGLISH.DAT", "Created: script.dat"], created);
	}

	[TestMethod]
	public void PluginDllInAnotherCasing_IsFound()
	{
		// The port warns about a plugin that has neither a plugin script nor a DLL in the engine folder.
		using TestWorkspace workspace = TestWorkspace.CreateCaseSensitive().WithBinaryFile("Plugin_X.dll", [0x4D, 0x5A]);

		var logger = new CompilerLog(conciseLogging: false);
		var diagnostics = new DiagnosticSink(logger);
		var registry = new PluginRegistry(scripts: [], engineDirectory: workspace.RootPath, paths: new PathResolver(), logger, diagnostics);

		registry.Declare(UnitRun.PluginLine(id: "2", name: "PLUGIN_X", location: new SourceLocation(1, File: workspace.ScriptFile)));
		registry.Declare(UnitRun.PluginLine(id: "3", name: "Plugin_Y", location: new SourceLocation(2, File: workspace.ScriptFile)));

		DiagnosticAssert.HasWarning(
			diagnostics: diagnostics.Recorded,
			line: 2,
			message: "The plugin 'Plugin_Y' was not found, because Plugin_Y.dll is missing",
			file: workspace.ScriptFile
		);

		Assert.HasCount(1, diagnostics.Recorded, "The DLL of PLUGIN_X is found as Plugin_X.dll.");
	}

	private static void WriteFile(string folder, string relativePath, string content)
	{
		string path = Path.Join(folder, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
	}
}
