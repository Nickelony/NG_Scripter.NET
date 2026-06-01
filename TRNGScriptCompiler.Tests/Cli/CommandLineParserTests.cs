using TRNGScriptCompiler.Cli;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Cli;

[TestClass]
public sealed class CommandLineParserTests
{
	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_LongOptions_MapToCompilerOptions()
	{
		CommandLineParseResult.Compile result = ParseCompile([
			@"C:\Engine",
			"--script-folder",
			@"C:\Engine\script",
			"--plugin-folder",
			@"C:\Plugins",
			"--main-language",
			"French",
			"--constants",
			@"C:\const\scripter_constants.txt",
			"--quiet",
			"--wait",
		]);

		Assert.AreEqual(@"C:\Engine", result.Options.EngineDirectory);
		Assert.AreEqual(@"C:\Engine\script", result.Options.ScriptDirectory);
		Assert.AreEqual(@"C:\Plugins", result.Options.PluginDirectory);
		Assert.AreEqual("French", result.Options.MainLanguage?.Text);
		Assert.AreEqual(@"C:\const\scripter_constants.txt", result.Options.ConstantsFilePath);
		Assert.IsTrue(result.ConciseLogging);
		Assert.IsTrue(result.WaitForKey);
		Assert.IsFalse(result.WriteLogFile);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_ShortAliases_MapToCompilerOptions()
	{
		CommandLineParseResult.Compile result = ParseCompile([
			@"C:\Engine",
			"-sf",
			@"C:\Scripts",
			"-pf",
			@"C:\PluginSrc",
			"-ml",
			"German",
			"-c",
			@"C:\constants.txt",
			"-q",
			"-w",
		]);

		Assert.AreEqual(@"C:\Scripts", result.Options.ScriptDirectory);
		Assert.AreEqual(@"C:\PluginSrc", result.Options.PluginDirectory);
		Assert.AreEqual("German", result.Options.MainLanguage?.Text);
		Assert.AreEqual(@"C:\constants.txt", result.Options.ConstantsFilePath);
		Assert.IsTrue(result.ConciseLogging);
		Assert.IsTrue(result.WaitForKey);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_VerboseAfterQuiet_ReenablesVerboseLogging()
	{
		CommandLineParseResult.Compile result = ParseCompile([@"C:\Engine", "-q", "--verbose"]);
		Assert.IsFalse(result.ConciseLogging, "The last logging flag wins.");
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("--main-language", "-ml", "Missing language value after main language option")]
	[DataRow("--script-folder", "-sf", "Missing path after script folder option")]
	[DataRow("--plugin-folder", "-pf", "Missing path after plugin folder option")]
	[DataRow("--constants", "-c", "Missing path after constants option")]
	public void Parse_MissingOptionValue_ReturnsError(string longForm, string shortForm, string message)
	{
		string[] options = [longForm, shortForm];

		foreach (string option in options)
		{
			string error = ParseError([@"C:\Engine", option]);
			Assert.AreEqual(message, error, $"'{option}' without a value must fail.");
		}
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("--frobnicate")]
	[DataRow("-x")]
	public void Parse_UnknownOption_ReturnsError(string option)
	{
		string error = ParseError([@"C:\Engine", option]);
		Assert.AreEqual($"Unknown option: {option}", error, "Unknown options must be rejected instead of silently ignored.");
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("--help")]
	[DataRow("-h")]
	[DataRow("--HELP")]
	[DataRow("-H")]
	[DataRow("-?")]
	[DataRow("/?")]
	public void Parse_HelpFlag_IsDetectedAnywhere(string helpFlag)
	{
		CommandLineParseResult result = CommandLineParser.Parse([@"C:\Engine", "--quiet", helpFlag]);
		Assert.IsInstanceOfType<CommandLineParseResult.Help>(result);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("--version")]
	[DataRow("--VERSION")]
	public void Parse_VersionFlag_IsDetectedAnywhere(string versionFlag)
	{
		CommandLineParseResult result = CommandLineParser.Parse([@"C:\Engine", "--quiet", versionFlag]);
		Assert.IsInstanceOfType<CommandLineParseResult.Version>(result);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_MissingEngineDirectory_ReturnsError() => Assert.AreEqual("No engine folder specified", ParseError([string.Empty]));

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_NoArguments_ReturnsError() => Assert.AreEqual("No engine folder specified", ParseError([]));

	[TestMethod]
	[TestCategory("IO")]
	public void Parse_Defaults_AutodetectScriptFolderAndBundledConstants()
	{
		using var workspace = TestWorkspace.CreateEmpty();

		CommandLineParseResult.Compile result = ParseCompile([workspace.RootPath]);

		string expectedScriptDirectory = Path.Combine(workspace.RootPath, "script");

		Assert.IsTrue(
			string.Equals(expectedScriptDirectory, result.Options.ScriptDirectory, StringComparison.OrdinalIgnoreCase),
			"The script folder must autodetect to <engine>/script."
		);

		Assert.AreEqual(workspace.RootPath, result.Options.PluginDirectory, "The plugin folder defaults to the engine folder.");

		Assert.AreEqual(
			Path.Combine(AppContext.BaseDirectory, "scripter_constants.txt"),
			result.Options.ConstantsFilePath,
			"The constants file defaults to the bundled scripter_constants.txt."
		);
	}

	// ── NG Center's command line ────────────────────────────────────────────

	// NG Center's command line names a Windows path, and only Windows splits a path at its backslashes, so elsewhere a test passes the same
	// path with the drive as its first folder and a '/' between folders.
	private static string Native(string windowsPath)
	{
		if (OperatingSystem.IsWindows())
			return windowsPath;

		string path = windowsPath.Length > 1 && windowsPath[1] == ':' ? "/" + windowsPath[0] + windowsPath[2..] : windowsPath;
		return path.Replace('\\', '/');
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_NGCenterCommandLine_TakesTheFolderAboveScriptAsTheEngine()
	{
		// NG Center's batch form (compila_script.bat): the folder of the path, less a last \script, is the engine folder
		// (Module1.bas:3727-3737), it compiles <TRLE>\script\SCRIPT.TXT (:8815), and it reads the switches (:3740-3747).
		CommandLineParseResult.Compile result = ParseCompile([Native(@"d:\trle_vista\script\script.txt"), "-NoMsgBox", "-Concise", "-Log"]);

		Assert.AreEqual(Native(@"d:\trle_vista"), result.Options.EngineDirectory);
		Assert.AreEqual(Native(@"d:\trle_vista\script"), result.Options.ScriptDirectory);
		Assert.IsTrue(result.ConciseLogging);
		Assert.IsTrue(result.WriteLogFile);

		// Without -NoWait, NG Center waits for a key at the end of a command-line compile (Principale.frm:6790, Module1.bas:8978).
		Assert.IsTrue(result.WaitForKey);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_NGCenterCommandLine_NoWaitAndLowerCaseSwitches()
	{
		// Module1.bas compares text without regard to case (Option Compare Text, Module1.bas:4), so "-nowait" and "\SCRIPT" match too.
		CommandLineParseResult.Compile result = ParseCompile([Native(@"C:\TRLE\SCRIPT\Script.TXT"), "-nowait", "-log"]);

		Assert.AreEqual(Native(@"C:\TRLE"), result.Options.EngineDirectory);
		Assert.IsFalse(result.WaitForKey);
		Assert.IsTrue(result.WriteLogFile);
		Assert.IsFalse(result.ConciseLogging);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_ScriptTxtOutsideAScriptFolder_TakesItsOwnFolderAsTheEngine()
	{
		// A folder that does not end in \script is the engine folder itself (Module1.bas:3733-3736).
		CommandLineParseResult.Compile result = ParseCompile([Native(@"C:\TRLE\script.txt"), "-NoWait"]);

		Assert.AreEqual(Native(@"C:\TRLE"), result.Options.EngineDirectory);
		Assert.AreEqual(Native(@"C:\TRLE\script"), result.Options.ScriptDirectory);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_NGCenterCommandLine_PathAfterTheSwitches()
	{
		// NG Center takes the first quoted string of its command line as the path, wherever it stands (Module1.bas:3258-3285), and finds a
		// switch anywhere (:3740-3747).
		CommandLineParseResult.Compile result = ParseCompile(["-NoWait", Native(@"C:\TRLE\script\script.txt")]);

		Assert.AreEqual(Native(@"C:\TRLE"), result.Options.EngineDirectory);
		Assert.IsFalse(result.WaitForKey);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_UnquotedPathWithSpaces_IsJoined()
	{
		// Without quotes NG Center takes the whole command line, and the folder of the path ends before the switches (Module1.bas:3284,
		// 3727-3737); the C runtime splits that path at its spaces.
		CommandLineParseResult.Compile result = ParseCompile([Native(@"C:\My"), Native(@"TRLE\script\script.txt"), "-Log"]);

		Assert.AreEqual(Native(@"C:\My TRLE"), result.Options.EngineDirectory);
		Assert.AreEqual(Native(@"C:\My TRLE\script"), result.Options.ScriptDirectory);
		Assert.IsTrue(result.WriteLogFile);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow(@"d:\TRLE - Copy\script\script.txt", @"d:\TRLE - Copy")]
	[DataRow(@"d:\TRLE -old\script\script.txt", @"d:\TRLE -old")]
	public void Parse_UnquotedPathWithAWordThatStartsWithAMinus_IsJoined(string commandLinePath, string engineDirectory)
	{
		// Without quotes NG Center takes the whole command line as the path (Module1.bas:3284) and matches a switch only as a whole word
		// (:3740-3747), so "-" and "-old\script\script.txt" are words of the path.
		CommandLineParseResult.Compile result = ParseCompile(["-Log", .. Native(commandLinePath).Split(' '), "-NoWait"]);

		Assert.AreEqual(Native(engineDirectory), result.Options.EngineDirectory);
		Assert.AreEqual(Native(engineDirectory + @"\script"), result.Options.ScriptDirectory);
		Assert.IsTrue(result.WriteLogFile);
		Assert.IsFalse(result.WaitForKey);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_UnknownWordAfterTheScriptPath_IsRejected()
	{
		// NG Center ignores a word that is no switch (Module1.bas:3740-3747); the port names it.
		Assert.AreEqual("Unknown option: -Quiet", ParseError([@"C:\TRLE\script\script.txt", "-Quiet"]));
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("-q")]
	[DataRow("-Log")]
	public void Parse_SecondPathAfterAnOption_IsRejected(string option)
	{
		// NG Center finds a switch anywhere and takes the whole line as the path (Module1.bas:3258-3285, 3740-3747), so it compiles
		// "C:\My -Log TRLE"; the port reads the switch, so the path ends before it.
		string error = ParseError([@"C:\My", option, @"TRLE\script\script.txt"]);
		Assert.AreEqual(@"'TRLE\script\script.txt' follows an option, so it is a second path after 'C:\My'; pass one path, and quote it", error);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_TextFileOtherThanScriptTxt_IsRejected()
	{
		// NG Center enters its command-line compile for any command line holding "script.txt" (Principale.frm:2927) and then compiles
		// <TRLE>\script\SCRIPT.TXT (Module1.bas:8815), so "myscript.txt" would compile another file than the one named.
		string path = Native(@"C:\TRLE\script\myscript.txt");
		string error = ParseError([path]);

		Assert.AreEqual(
			$"NG Center's command line compiles script.txt in the script folder, so '{path}' names another file; pass the path of script.txt",
			error
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_WaitAndNoWait_IsRejected() =>
		Assert.AreEqual("--wait and -NoWait contradict each other; keep one of them", ParseError([@"C:\Engine", "--wait", "-NoWait"]));

	private static CommandLineParseResult.Compile ParseCompile(string[] args)
	{
		CommandLineParseResult result = CommandLineParser.Parse(args);
		return Assert.IsInstanceOfType<CommandLineParseResult.Compile>(result, (result as CommandLineParseResult.Error)?.Message);
	}

	private static string ParseError(string[] args) => Assert.IsInstanceOfType<CommandLineParseResult.Error>(CommandLineParser.Parse(args)).Message;
}
