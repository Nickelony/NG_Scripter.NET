using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Tests.Support;

/// <summary>The single shared fluent builder for disposable TRNG project workspaces on disk.</summary>
/// <remarks>
/// <see cref="TestAssembly"/> removes the per-assembly temp root, and a file is written when added, so a test
/// can change it between compiles.
/// </remarks>
public sealed class TestWorkspace : IDisposable
{
	/// <summary>
	/// The deterministic seed used whenever a test compares compiled bytes, because an unseeded compile differs
	/// in its padding (PD-07).
	/// </summary>
	public const double DefaultDeterministicSeed = 99999;

	/// <summary>Minimal <c>Objects.h</c> with one slot and one static constant (ordinal values 0).</summary>
	public const string ObjectsHContent =
		"enum object_types\r\n{\r\n    SLOT_TEST = 9,\r\n};\r\n\r\nenum static_types\r\n{\r\n    STATIC_TEST = 5,\r\n};\r\n";

	/// <summary>
	/// An <c>English.txt</c> that holds the level name of <see cref="TestScripts.LanguageAndLevel"/> after a string.
	/// </summary>
	public const string DefaultEnglishContent = "[Strings]\nTest String\nTest Level\n";

	/// <summary>
	/// The <c>English.txt</c> content that holds the level name of <see cref="TestScripts.LanguageAndLevel"/>.
	/// </summary>
	public const string CompilableLanguageContent = "[Strings]\nTest Level\n";

	/// <summary>
	/// The extension sections VB6 expects, three extensions each, so a script that holds them draws
	/// no extension warning.
	/// </summary>
	public const string ExtensionSectionsContent =
		"[PSXExtensions]\nLevel= .PSX\nCut= .PSX\nFMV= .FMV\n[PCExtensions]\nLevel= .TR4\nCut= .TR4\nFMV= .BIK\n";

	/// <summary>An <c>Objects.h</c> whose items assign no value, so it draws no warning.</summary>
	public const string CleanObjectsHContent =
		"enum object_types\r\n{\r\n    SLOT_TEST,\r\n};\r\n\r\nenum static_types\r\n{\r\n    STATIC_TEST,\r\n};\r\n";

	/// <summary>The line on which <see cref="ScriptWithOptionsCommand"/> puts its command.</summary>
	public const int OptionsCommandLine = 4;

	/// <summary>Gets this test run's temp root, which holds every workspace of the run.</summary>
	/// <remarks>
	/// Each run has its own root, so a run's assembly cleanup never deletes the workspaces of a run still in progress.
	/// The root sits below the test output folder, for the reason that docs/TESTING.md gives.
	/// </remarks>
	public static string TempRoot { get; } = Path.Combine(AppContext.BaseDirectory, "workspaces", Guid.NewGuid().ToString("N"));

	/// <summary>Creates a workspace in a fresh unique temp directory and writes the main script file.</summary>
	/// <param name="scriptContent">
	/// The <c>script/Script.txt</c> content.
	/// Uses <see cref="TestScripts.LanguageAndLevel"/> when <see langword="null"/>.
	/// </param>
	/// <returns>The new workspace.</returns>
	public static TestWorkspace Create(string? scriptContent = null)
	{
		var workspace = new TestWorkspace();
		return workspace.WithScript(scriptContent ?? TestScripts.LanguageAndLevel);
	}

	/// <summary>
	/// Creates a workspace that compiles, because its language file holds the level name its script gives.
	/// </summary>
	/// <param name="scriptContent">
	/// The <c>script/Script.txt</c> text; <see langword="null"/> uses <see cref="TestScripts.LanguageAndLevel"/>.
	/// </param>
	/// <param name="languageContent">
	/// The <c>script/English.txt</c> text; <see langword="null"/> uses <see cref="CompilableLanguageContent"/>.
	/// </param>
	/// <returns>The new workspace.</returns>
	public static TestWorkspace CreateCompilable(string? scriptContent = null, string? languageContent = null) =>
		Create(scriptContent ?? TestScripts.LanguageAndLevel).WithLanguageFile("English.txt", languageContent ?? CompilableLanguageContent);

	/// <summary>
	/// Creates a workspace that compiles without a diagnostic, whose level ends in the lines given, so a test
	/// can assert that those lines draw none.
	/// </summary>
	public static TestWorkspace CreateClean(string levelLines = "") =>
		CreateCompilable(ExtensionSectionsContent + TestScripts.LanguageAndLevel + levelLines).WithObjectsH(CleanObjectsHContent);

	/// <summary>
	/// Builds <c>Script.txt</c> content whose <c>[Options]</c> section holds
	/// the one command given, on <see cref="OptionsCommandLine"/>, ahead of a level that compiles.
	/// </summary>
	public static string ScriptWithOptionsCommand(string command) => TestScripts.Language + "[Options]\n" + command + "\n" + TestScripts.Level;

	/// <summary>
	/// Builds the content of a language file whose <c>[Strings]</c> section holds the strings given, one a line.
	/// </summary>
	public static string LanguageFile(params string[] strings) => "[Strings]\n" + string.Concat(strings.Select(value => value + "\n"));

	/// <summary>
	/// Builds the content of a language file like <see cref="LanguageFile"/>, followed by an <c>[ExtraNG]</c> section
	/// that holds the entries given, one a line.
	/// </summary>
	public static string LanguageFileWithExtraNg(string[] strings, params (int Index, string Text)[] extraNg) =>
		LanguageFile(strings) + "[ExtraNG]\n" + string.Concat(extraNg.Select(entry => $"{entry.Index}: {entry.Text}\n"));

	/// <summary>
	/// Builds an <c>Animation=</c> line for the identifier given, with an array of the length given,
	/// which makes the command 8 plus that length words long.
	/// </summary>
	public static string AnimationCommand(int id, int arrayLength) =>
		$"Animation= {id},1,2,3,4,5,6,{string.Join(',', Enumerable.Repeat(1, arrayLength))}\n";

	/// <summary>Creates the compiler a test runs, with the concise log that <see cref="Compile"/> uses.</summary>
	public static TRNGCompiler CreateCompiler() => new() { ConciseLogging = true };

	/// <summary>
	/// Compiles a script in a fresh workspace that holds the stock TRLE <c>Objects.h</c>, and returns the result
	/// and the bytes of its <c>Script.dat</c>.
	/// </summary>
	/// <param name="scriptContent">
	/// The <c>script/Script.txt</c> text, whose level name <c>Test Level</c> the language file holds.
	/// </param>
	/// <param name="scriptDat">The bytes of <c>Script.dat</c>, empty when the compile wrote none.</param>
	/// <param name="prepare">
	/// Adds more files to the workspace before the compile, or <see langword="null"/> to add none.
	/// </param>
	public static TestCompileResult CompileOnStockObjectsH(string scriptContent, out byte[] scriptDat, Action<TestWorkspace>? prepare = null)
	{
		using TestWorkspace workspace = CreateCompilable(scriptContent);

		// Every corpus workspace holds the stock Objects.h of the VB6 sources.
		workspace.WithStockObjectsH();
		prepare?.Invoke(workspace);

		TestCompileResult result = workspace.Compile();
		string path = workspace.PathOf("Script.dat");

		scriptDat = result.Success && File.Exists(path) ? File.ReadAllBytes(path) : [];

		return result;
	}

	/// <summary>
	/// Compiles a script as <see cref="CompileOnStockObjectsH"/> does, asserts that the compile succeeds,
	/// and returns the bytes of its <c>Script.dat</c>.
	/// </summary>
	public static byte[] CompileScriptDatOnStockObjectsH(string scriptContent, Action<TestWorkspace>? prepare = null)
	{
		TestCompileResult result = CompileOnStockObjectsH(scriptContent, out byte[] scriptDat, prepare);
		Assert.IsTrue(result.Success, result.Log);

		return scriptDat;
	}

	/// <summary>Creates a workspace in a fresh unique temp directory without writing any files.</summary>
	/// <returns>The new empty workspace.</returns>
	public static TestWorkspace CreateEmpty() => new();

	/// <summary>
	/// Creates an empty workspace whose folders hold apart two names that differ only in case, as a Linux file
	/// system does.
	/// </summary>
	/// <remarks>
	/// On Windows, <c>fsutil</c> marks the root case-sensitive and its new folders inherit the mark; a refused mark
	/// makes the test inconclusive.
	/// </remarks>
	public static TestWorkspace CreateCaseSensitive()
	{
		var workspace = new TestWorkspace();

		// fsutil marks an empty folder alone, so the script folder is created again after the mark.
		Directory.Delete(workspace.ScriptDirectory);

		if (OperatingSystem.IsWindows())
			MarkCaseSensitive(workspace.RootPath);

		Directory.CreateDirectory(workspace.ScriptDirectory);

		if (!HoldsCaseVariantsApart(workspace.ScriptDirectory))
		{
			workspace.Dispose();
			Assert.Inconclusive("This file system does not hold apart two names that differ only in case.");
		}

		return workspace;
	}

	private static void MarkCaseSensitive(string emptyDirectory)
	{
		var startInfo = new ProcessStartInfo("fsutil.exe") { CreateNoWindow = true, UseShellExecute = false };

		foreach (string argument in new[] { "file", "setCaseSensitiveInfo", emptyDirectory, "enable" })
			startInfo.ArgumentList.Add(argument);

		try
		{
			using var process = Process.Start(startInfo);

			if (process is not null && !process.WaitForExit(15000))
				process.Kill(entireProcessTree: true);
		}
		catch (Win32Exception)
		{
			// Without fsutil the folder stays case-insensitive, which the probe of the caller reports.
		}
	}

	private static bool HoldsCaseVariantsApart(string directory)
	{
		string probe = Path.Combine(directory, "case-probe");
		File.WriteAllText(probe, string.Empty);

		bool apart = !File.Exists(Path.Combine(directory, "CASE-PROBE"));
		File.Delete(probe);

		return apart;
	}

	/// <summary>
	/// Copies every file of a directory tree into this workspace at the same relative path, and returns this workspace.
	/// </summary>
	/// <remarks>
	/// The parity tests copy the checked-in fixture this way before they compile it.
	/// </remarks>
	public TestWorkspace CopyTreeFrom(string sourceDirectory)
	{
		foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			string relativePath = Path.GetRelativePath(sourceDirectory, file);
			string destination = Path.Combine(RootPath, relativePath);

			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			File.Copy(file, destination, overwrite: true);
		}

		return this;
	}

	private TestWorkspace()
	{
		RootPath = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(RootPath, "script"));
	}

	/// <summary>Gets the engine folder of this workspace.</summary>
	public string RootPath { get; }

	/// <summary>Gets the script folder (<c>{RootPath}/script</c>).</summary>
	public string ScriptDirectory => Path.Combine(RootPath, "script");

	/// <summary>
	/// Gets the main script (<c>{RootPath}/script/Script.txt</c>), the file an error of the script names.
	/// </summary>
	public string ScriptFile => Path.Combine(ScriptDirectory, "Script.txt");

	/// <summary>Writes the main <c>script/Script.txt</c> file.</summary>
	public TestWorkspace WithScript(string content) => WithTextFile(Path.Combine("script", "Script.txt"), content);

	/// <summary>Writes a language file into the script folder.</summary>
	public TestWorkspace WithLanguageFile(string fileName, string content) => WithTextFile(Path.Combine("script", fileName), content);

	/// <summary>
	/// Writes <c>script/English.txt</c> with the supplied content (default: a string table containing
	/// <c>Test String</c> and <c>Test Level</c>).
	/// </summary>
	public TestWorkspace WithEnglishLanguageFile(string? content = null) => WithLanguageFile("English.txt", content ?? DefaultEnglishContent);

	/// <summary>
	/// Writes a text file relative to the engine folder, in Windows-1252 (the encoding VB6 read)
	/// unless another is requested.
	/// </summary>
	public TestWorkspace WithTextFile(string relativePath, string content, Encoding? encoding = null)
	{
		string path = PathOf(relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content, encoding ?? Vb6Ansi.Encoding);
		return this;
	}

	/// <summary>Writes a binary file relative to the engine folder.</summary>
	public TestWorkspace WithBinaryFile(string relativePath, byte[] bytes)
	{
		string path = PathOf(relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, bytes);
		return this;
	}

	/// <summary>Writes a binary file relative to the script folder.</summary>
	public TestWorkspace WithScriptBinaryFile(string relativePath, byte[] bytes) => WithBinaryFile(Path.Combine("script", relativePath), bytes);

	/// <summary>Writes <c>Objects.h</c> (default: <see cref="ObjectsHContent"/>).</summary>
	public TestWorkspace WithObjectsH(string? content = null) => WithTextFile("Objects.h", content ?? ObjectsHContent);

	/// <summary>Writes the stock TRLE <c>Objects.h</c> (<see cref="TestFixtures.StockObjectsHPath"/>).</summary>
	public TestWorkspace WithStockObjectsH() => WithObjectsH(File.ReadAllText(TestFixtures.StockObjectsHPath));

	/// <summary>Writes a <c>Plugin*.script</c> file into the plugin folder, which is the engine folder here.</summary>
	public TestWorkspace WithPluginFile(string fileName, string content) => WithTextFile(fileName, content);

	/// <summary>Creates fully-resolved compile options for this workspace.</summary>
	/// <param name="mainLanguage">The requested main language, or <see langword="null"/>.</param>
	/// <param name="deterministicSeed">A pinned PRNG seed. Defaults to <see cref="DefaultDeterministicSeed"/>.</param>
	public TRNGCompilerOptions CreateOptions(string? mainLanguage = "English", double deterministicSeed = DefaultDeterministicSeed)
	{
		return TRNGCompilerOptions.CreateDefaults(RootPath, mainLanguage) with { DeterministicSeed = deterministicSeed };
	}

	/// <summary>Runs a full <see cref="TRNGCompiler.Compile"/> against this workspace.</summary>
	/// <param name="options">The compile options. Defaults to <see cref="CreateOptions"/>.</param>
	/// <param name="compiler">
	/// An existing compiler instance to reuse, or <see langword="null"/> for a fresh one.
	/// </param>
	public TestCompileResult Compile(TRNGCompilerOptions? options = null, TRNGCompiler? compiler = null)
	{
		compiler ??= CreateCompiler();
		options ??= CreateOptions();

		var entries = new List<LogEntry>();
		void capture(object? _, LogEntryProducedEventArgs e) => entries.Add(e.Entry);

		compiler.LogEntryProduced += capture;

		try
		{
			CompileResult result = compiler.Compile(options);
			return new TestCompileResult(Success: result.Success, Log: result.Log, Diagnostics: result.Diagnostics, entries);
		}
		finally
		{
			compiler.LogEntryProduced -= capture;
		}
	}

	/// <summary>Runs an analysis-only compilation against this workspace.</summary>
	/// <param name="options">The compile options. Defaults to <see cref="CreateOptions"/>.</param>
	/// <param name="overlay">Optional in-memory file overlay.</param>
	/// <param name="compiler">
	/// An existing compiler instance to reuse, or <see langword="null"/> for a fresh one.
	/// </param>
	public AnalysisResult Analyze(TRNGCompilerOptions? options = null, VirtualFileSystem? overlay = null, TRNGCompiler? compiler = null)
	{
		compiler ??= CreateCompiler();
		options ??= CreateOptions();

		return compiler.Analyze(options, overlay);
	}

	/// <summary>Reads <c>Script.dat</c> from the engine folder.</summary>
	public byte[] ReadScriptDat() => File.ReadAllBytes(Path.Combine(RootPath, "Script.dat"));

	/// <summary>Reads a compiled output file (for example <c>English.dat</c>) from the engine folder.</summary>
	public byte[] ReadOutput(string fileName) => File.ReadAllBytes(PathOf(fileName));

	/// <summary>Gets the full path of a file relative to the engine folder.</summary>
	/// <remarks>
	/// A backslash in the relative path separates folders on every system, as it does in a script.
	/// </remarks>
	public string PathOf(string relativePath) => Path.Combine(RootPath, relativePath.Replace('\\', Path.DirectorySeparatorChar));

	/// <summary>
	/// Runs an action while a handle holds a file of this workspace open without sharing it, so the file
	/// cannot be read.
	/// </summary>
	/// <typeparam name="T">The type of the action's result.</typeparam>
	/// <param name="relativePath">The path of the file to lock, relative to the engine folder.</param>
	/// <param name="action">The action to run while the lock holds.</param>
	/// <returns>The result of the action.</returns>
	/// <remarks>
	/// <c>FileShare.None</c> locks the file on Linux too, where .NET takes an exclusive advisory lock
	/// that its own reads respect.
	/// </remarks>
	public T WithFileLocked<T>(string relativePath, Func<T> action)
	{
		using var handle = new FileStream(path: PathOf(relativePath), mode: FileMode.Open, access: FileAccess.Read, share: FileShare.None);
		return action();
	}

	/// <summary>Runs an action while a folder of this workspace denies the current user the right to list it.</summary>
	/// <typeparam name="T">The type of the action's result.</typeparam>
	/// <param name="relativePath">The path of the folder to deny, relative to the engine folder.</param>
	/// <param name="action">The action to run while the rule holds.</param>
	/// <returns>The result of the action.</returns>
	/// <remarks>
	/// The rule is a Windows access rule, which no other system has, so a test that calls
	/// this carries <c>[OSCondition(OperatingSystems.Windows)]</c>.
	/// </remarks>
	[SupportedOSPlatform("windows")]
	public T WithListingDenied<T>(string relativePath, Func<T> action)
	{
		var folder = new DirectoryInfo(PathOf(relativePath));
		SecurityIdentifier user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The test user has no SID.");
		var denyListing = new FileSystemAccessRule(user, FileSystemRights.ListDirectory, AccessControlType.Deny);

		DirectorySecurity security = folder.GetAccessControl();
		security.AddAccessRule(denyListing);
		folder.SetAccessControl(security);

		try
		{
			return action();
		}
		finally
		{
			security.RemoveAccessRule(denyListing);
			folder.SetAccessControl(security);
		}
	}

	/// <summary>Recursively deletes the workspace directory, ignoring I/O errors.</summary>
	public void Dispose()
	{
		try
		{
			if (Directory.Exists(RootPath))
				Directory.Delete(RootPath, true);
		}
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}
}

/// <summary>
/// The captured outcome of a compilation run: its success, full log text, hints, warnings and errors, and log entries.
/// </summary>
/// <remarks>
/// <c>Success</c> means no fatal error, and <c>Entries</c> holds the log lines
/// as the compiler streamed them, for a test of a non-diagnostic line.
/// </remarks>
public sealed record TestCompileResult(bool Success, string Log, IReadOnlyList<CompilerDiagnostic> Diagnostics, IReadOnlyList<LogEntry> Entries);
