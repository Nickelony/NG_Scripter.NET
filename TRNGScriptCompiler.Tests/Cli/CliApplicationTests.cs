using TRNGScriptCompiler.Cli;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Cli;

[TestClass]
public sealed class CliApplicationTests
{
	private static (CapturedConsole Console, CliRunResult Result) RunWithUnknownOption()
	{
		var console = new CapturedConsole();
		return (console, console.CreateApplication().Run([@"C:\Engine", "--bogus"]));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Run_Help_WritesHelpTextAndReturnsZero()
	{
		var console = new CapturedConsole();

		CliRunResult result = console.CreateApplication().Run(["--help"]);

		Assert.AreEqual(0, result.ExitCode);
		Assert.IsFalse(result.WaitForKey);
		Assert.AreEqual(CliApplication.HelpText, console.AllText);

		string[] helpLines =
		[
			"Usage:",
			"  --main-language <language>  Set the language whose .dat ships with the game.",
			"  --version                   Show the version and exit.",
			"  -NoMsgBox                   Accepted; the console shows no message box.",
		];

		CollectionAssert.IsSubsetOf(helpLines, CliApplication.HelpText.ReplaceLineEndings("\n").Split('\n'));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Run_Version_WritesTheVersionAloneAndReturnsZero()
	{
		var console = new CapturedConsole();

		CliRunResult result = console.CreateApplication().Run(["--version"]);

		Version? assemblyVersion = typeof(CliApplication).Assembly.GetName().Version;
		Assert.IsNotNull(assemblyVersion);

		Assert.AreEqual(0, result.ExitCode);
		Assert.IsFalse(result.WaitForKey);
		Assert.AreEqual($"NG_Scripter {assemblyVersion.ToString(fieldCount: 3)}", CliApplication.VersionText);

		string[] expectedOutput = [CliApplication.VersionText];

		Assert.AreSequenceEqual(expectedOutput, console.OutputLines, "The version prints alone, with no banner.");
		Assert.AreEqual(string.Empty, console.ErrorText);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Help_ShowsItsExamplePathsWithTheSeparatorOfTheHost()
	{
		char s = Path.DirectorySeparatorChar;

		string[] helpLines =
		[
			$"  NG_Scripter <path>{s}script{s}script.txt [options]",
			$"  <path>{s}script{s}script.txt    NG Center's form: the engine folder is the folder above script.",
			$"  -pf                         Defaults to <engine-folder>{s}NG_Center, or to <engine-folder> without it.",
			$"  -Log                        Also write the log to <engine-folder>{s}LastCompilerLog.txt.",
		];

		CollectionAssert.IsSubsetOf(helpLines, CliApplication.HelpText.ReplaceLineEndings("\n").Split('\n'));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Run_Help_PrintsNoBannerAndNothingToTheErrorWriter()
	{
		var console = new CapturedConsole();

		console.CreateApplication().Run([@"C:\Engine", "--quiet", "-h"]);

		Assert.AreEqual(CliApplication.HelpText, console.OutputText, "The output of help opens with the help and holds no banner.");
		Assert.AreEqual(string.Empty, console.ErrorText);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Run_ParseError_WritesErrorInRedAndReturnsOne()
	{
		(CapturedConsole console, CliRunResult result) = RunWithUnknownOption();

		Assert.AreEqual(1, result.ExitCode);
		Assert.IsFalse(result.WaitForKey);
		Assert.AreEqual("Error: Unknown option: --bogus" + Environment.NewLine, console.TextIn(ConsoleColor.Red));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Run_ParseError_WritesTheErrorToTheErrorWriterAndNoBanner()
	{
		(CapturedConsole console, _) = RunWithUnknownOption();

		Assert.AreEqual("Error: Unknown option: --bogus" + Environment.NewLine, console.ErrorText);
		Assert.AreEqual(string.Empty, console.OutputText, "A command line that does not parse starts no compile, so it prints no banner.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_CompilableWorkspace_PrintsSuccessAndReturnsZero()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		var console = new CapturedConsole();

		CliRunResult result = console.CreateApplication().Run([workspace.RootPath, "--quiet"]);

		Assert.AreEqual(0, result.ExitCode, console.AllText);
		Assert.IsFalse(result.WaitForKey);

		Assert.AreSequenceEqual(new[] { "Compilation completed successfully" }, console.LinesIn(ConsoleColor.Green));
		Assert.IsTrue(File.Exists(workspace.PathOf("Script.dat")));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_CompilableWorkspace_OpensWithTheBannerAndWritesNothingToTheErrorWriter()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		var console = new CapturedConsole();

		console.CreateApplication().Run([workspace.RootPath, "--quiet"]);

		Assert.AreEqual(CliApplication.BannerText, Start(console.OutputText, CliApplication.BannerText.Length));
		Assert.AreEqual(string.Empty, console.ErrorText);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_WarningInTheScript_StaysOnTheOutputWriter()
	{
		// The workspace holds no Objects.h, which draws a warning.
		using var workspace = TestWorkspace.CreateCompilable();
		var console = new CapturedConsole();

		CliRunResult result = console.CreateApplication().Run([workspace.RootPath, "--quiet"]);

		Assert.AreEqual(0, result.ExitCode, console.AllText);

		string[] warnings = console.LinesIn(ConsoleColor.Yellow);

		Assert.IsNotEmpty(warnings, console.AllText);
		Assert.AreEqual($"WARNING: General: Objects.h was not found: {workspace.PathOf("Objects.h")}", warnings[0]);
		Assert.IsEmpty(warnings.Except(console.OutputLines), "Every warning line is on standard output.");
		Assert.AreEqual(string.Empty, console.ErrorText);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_FailingWorkspace_PrintsCompilationFailedOnce()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Level]\nName= Missing Name\nLevel= data\\test,0\n");
		var console = new CapturedConsole();

		CliRunResult result = console.CreateApplication().Run([workspace.RootPath, "-w"]);

		Assert.AreEqual(1, result.ExitCode);
		Assert.IsTrue(result.WaitForKey, "The wait flag holds when the compile fails.");
		Assert.AreEqual("Compilation failed", console.AllLines[^1], console.AllText);

		string[] redLines =
		[
			"ERROR: Line 3 of Script.txt: LoadCamera= command is required in level section 'Missing Name'",
			"ERROR: Line 4 of Script.txt: Cannot find level name in language file: Missing Name",
			"Compilation failed",
		];

		Assert.AreSequenceEqual(redLines, console.LinesIn(ConsoleColor.Red), console.AllText);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_FailingWorkspace_WritesErrorsAndTheOutcomeToTheErrorWriterAndTheRestToTheOutputWriter()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Level]\nName= Missing Name\nLevel= data\\test,0\n");
		var console = new CapturedConsole();

		console.CreateApplication().Run([workspace.RootPath]);

		string[] errorLines =
		[
			"ERROR: Line 3 of Script.txt: LoadCamera= command is required in level section 'Missing Name'",
			"ERROR: Line 4 of Script.txt: Cannot find level name in language file: Missing Name",
			"Compilation failed",
		];

		Assert.AreSequenceEqual(errorLines, console.ErrorLines, console.AllText);

		string[] outputLines = console.OutputLines;

		Assert.AreEqual(CliApplication.BannerText, Start(console.OutputText, CliApplication.BannerText.Length));
		Assert.IsEmpty(outputLines.Intersect(errorLines), "No error line is repeated on standard output.");

		// The banner is followed by an empty line, and the log opens after it.
		string[] afterBanner = console.OutputText[CliApplication.BannerText.Length..].ReplaceLineEndings("\n").Split('\n');

		Assert.AreSequenceEqual(new[] { string.Empty, "**** START ANALYZING ****" }, afterBanner[..2]);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_FailingWorkspaceWithLog_WritesEveryLineToTheLogFileInOrder()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Level]\nName= Missing Name\nLevel= data\\test,0\n");
		var console = new CapturedConsole();

		console.CreateApplication().Run([workspace.RootPath, "-Log"]);

		// The console holds the banner, and an empty line before the log and another before the outcome, which the log file does not.
		string[] consoleLines = console.AllText[CliApplication.BannerText.Length..].ReplaceLineEndings("\n").Split('\n')[..^1];
		string[] fileLines = File.ReadAllLines(workspace.PathOf(CliLogFile.FileName));

		string[] expectedLines = [string.Empty, .. fileLines[..^1], string.Empty, fileLines[^1]];

		Assert.AreSequenceEqual(expectedLines, consoleLines);
		Assert.AreEqual("Compilation failed", fileLines[^1]);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_CanceledBeforeThePublication_PublishesNothingAndReturns130()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		using var cancellation = new CancellationTokenSource();
		var console = new CapturedConsole();

		cancellation.Cancel();

		CliRunResult result = console.CreateApplication(cancellation.Token).Run([workspace.RootPath, "--wait"]);

		Assert.AreEqual(130, result.ExitCode, console.AllText);
		Assert.IsFalse(result.WaitForKey, "A canceled compile has no result to wait for.");
		Assert.IsFalse(File.Exists(workspace.PathOf("Script.dat")));
		Assert.AreSequenceEqual(new[] { "Compilation canceled" }, console.ErrorLines);
		Assert.IsEmpty(console.LinesIn(ConsoleColor.Green));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_CanceledWithLog_WritesTheCanceledOutcomeToTheLogFile()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		using var cancellation = new CancellationTokenSource();
		var console = new CapturedConsole();

		cancellation.Cancel();

		CliRunResult result = console.CreateApplication(cancellation.Token).Run([workspace.RootPath, "-Log"]);

		Assert.AreEqual(130, result.ExitCode, console.AllText);
		Assert.AreEqual("Compilation canceled", File.ReadAllLines(workspace.PathOf(CliLogFile.FileName))[^1]);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_NGCenterCommandLineWithAMissingFolder_FailsNamingTheEngineFolder()
	{
		// NG Center compiles the engine folder it saved at its last run when the folder of the path does not exist (Module1.bas:3729-3737).
		using var workspace = TestWorkspace.CreateEmpty();
		string engineDirectory = workspace.PathOf("Missing TRLE");
		var console = new CapturedConsole();

		CliRunResult result = console.CreateApplication().Run([Path.Combine(engineDirectory, "script", "script.txt"), "-NoWait"]);

		Assert.AreEqual(1, result.ExitCode, console.AllText);

		string[] redLines =
		[
			$"ERROR: General: Engine folder was not found: {engineDirectory}",
			$"ERROR: General: Script folder was not found: {Path.Combine(engineDirectory, "script")}",
			"Compilation failed",
		];

		Assert.AreSequenceEqual(redLines, console.LinesIn(ConsoleColor.Red), console.AllText);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_NGCenterCommandLineWithLog_CompilesAndWritesTheLogFile()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		var console = new CapturedConsole();

		CliRunResult result = console
			.CreateApplication()
			.Run([Path.Combine(workspace.ScriptDirectory, "script.txt"), "-NoMsgBox", "-Concise", "-Log", "-NoWait"]);

		Assert.AreEqual(0, result.ExitCode, console.AllText);
		Assert.IsFalse(result.WaitForKey);
		Assert.IsTrue(File.Exists(workspace.PathOf("Script.dat")));

		string[] logLines = File.ReadAllText(workspace.PathOf(CliLogFile.FileName)).Split(Environment.NewLine);

		Assert.AreEqual("**** START ANALYZING ****", logLines[0], "The log file opens with the first log line.");
		Assert.AreEqual("******** COMPLETED ********", logLines[^3], "The compiler's last log line precedes the outcome.");

		Assert.AreEqual("Compilation completed successfully", logLines[^2], "The outcome is the last line.");
		Assert.AreEqual(string.Empty, logLines[^1], "The last line ends in a line break.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_LogFileThatCannotBeWritten_FailsTheRun()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		Directory.CreateDirectory(workspace.PathOf(CliLogFile.FileName));
		var console = new CapturedConsole();

		CliRunResult result = console.CreateApplication().Run([workspace.RootPath, "-Log"]);

		Assert.AreEqual(1, result.ExitCode);

		// The line ends in the operating system's message, which the test cannot know.
		string error = console.LinesIn(ConsoleColor.Red).Single();
		string prefix = $"Error: The log file {workspace.PathOf(CliLogFile.FileName)} could not be written: ";

		Assert.AreEqual(prefix, Start(error, prefix.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_NoArguments_PromptsForThePathsCompilesAndWaits()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		var console = new CapturedConsole(workspace.RootPath + "\n\n\n");

		CliRunResult result = console.CreateApplication().Run([]);

		Assert.AreEqual(0, result.ExitCode, console.AllText);
		Assert.IsTrue(result.WaitForKey, "An interactive session always waits.");

		// A prompt ends in no line break and the reader's answers are not echoed, so the prompts share a line.
		string[] lines = console.AllText[CliApplication.BannerText.Length..].ReplaceLineEndings("\n").Split('\n');

		Assert.AreEqual(
			CliApplication.BannerText,
			Start(console.AllText, CliApplication.BannerText.Length),
			"An interactive session opens with the banner."
		);

		Assert.AreEqual(
			$"Enter engine folder path: Enter script folder path (press Enter to autodetect):   Autodetect -> {workspace.ScriptDirectory}",
			lines[0]
		);

		Assert.AreEqual("Enter plugin folder path (press Enter for NG_Center, or the engine folder without it): ", lines[1]);
		Assert.AreEqual("Compilation completed successfully", lines[^2]);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("")]
	[DataRow("\"\"")]
	[DataRow(" \" \" ")]
	public void Run_NoArgumentsAndAnEmptyEngine_ReturnsOneWithoutWaiting(string engineAnswer)
	{
		var console = new CapturedConsole(engineAnswer + "\n");

		CliRunResult result = console.CreateApplication().Run([]);

		Assert.AreEqual(1, result.ExitCode);
		Assert.IsFalse(result.WaitForKey);

		Assert.AreSequenceEqual(new[] { "Error: No engine folder specified" }, console.ErrorLines);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Run_NoArguments_StartsTheCompileOnlyAfterTheLastPrompt()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		var console = new CapturedConsole(workspace.RootPath + "\n\n\n");

		int starts = 0;
		string textAtStart = string.Empty;

		void CompileStarted()
		{
			starts++;
			textAtStart = console.AllText;
		}

		console.CreateApplication(compileStarted: CompileStarted).Run([]);

		Assert.AreEqual(1, starts);
		string lastLine = textAtStart.ReplaceLineEndings("\n").Split('\n')[^1];

		Assert.AreEqual(
			"Enter plugin folder path (press Enter for NG_Center, or the engine folder without it): ",
			lastLine,
			"Every prompt came first."
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Run_NoArgumentsAndAnEmptyEngine_NeverStartsACompile()
	{
		var console = new CapturedConsole("\n");

		int starts = 0;

		console.CreateApplication(compileStarted: () => starts++).Run([]);

		Assert.AreEqual(0, starts);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Run_ParseError_NeverStartsACompile()
	{
		var console = new CapturedConsole();

		int starts = 0;

		console.CreateApplication(compileStarted: () => starts++).Run([@"C:\Engine", "--bogus"]);

		Assert.AreEqual(0, starts);
	}

	// The first characters of a text, so an exact comparison can pin a line whose end the test cannot know.
	private static string Start(string text, int length) => text[..Math.Min(length, text.Length)];
}
