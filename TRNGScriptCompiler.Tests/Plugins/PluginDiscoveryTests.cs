using System.Runtime.Versioning;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Plugins;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Plugins;

[TestClass]
[TestCategory("IO")]
public sealed class PluginDiscoveryTests
{
	private const string Script = "<START_CONSTANTS>\r\nPLUGIN_CONST:$40\r\n<END>\r\n";

	private static (IReadOnlyList<PluginScript> Scripts, IReadOnlyList<LogEntry> Entries, DiagnosticSink Diagnostics) Discover(string folder)
	{
		var entries = new List<LogEntry>();
		var logger = new CompilerLog(conciseLogging: false, entries.Add);
		var diagnostics = new DiagnosticSink(logger);

		return (PluginDiscovery.Discover(folder, overlay: null, logger, diagnostics), entries, diagnostics);
	}

	[TestMethod]
	public void ScriptsOfTheFolderAndOfItsImmediateSubfolders_AreRead()
	{
		// NG Center reads the top level of its folder (TrovaFiles, MioModulo.bas:2056-2075), and its installer extracts a Plugin_X\
		// subfolder (Dialog_InstallPlugin.frm:222-262); a deeper folder holds no installed plugin.
		using TestWorkspace workspace = TestWorkspace
			.CreateEmpty()
			.WithTextFile("Plugin_Top.script", Script)
			.WithTextFile(Path.Combine("Plugin_Sub", "Plugin_Sub.script"), Script)
			.WithTextFile(Path.Combine("Plugin_Sub", "Backup", "Plugin_Deep.script"), Script);

		(IReadOnlyList<PluginScript> scripts, _, DiagnosticSink diagnostics) = Discover(workspace.RootPath);

		Assert.AreSequenceEqual(["Plugin_Sub", "Plugin_Top"], scripts.Select(script => script.Name));
		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);
	}

	[TestMethod]
	public void TopLevelScript_WinsOverASubfolderCopyOfItsName_WithAVerboseNoteWhenTheyDiffer()
	{
		using TestWorkspace workspace = TestWorkspace
			.CreateEmpty()
			.WithTextFile("Plugin_X.script", Script)
			.WithTextFile(Path.Combine("Plugin_X", "Plugin_X.script"), "<START_CONSTANTS>\r\nPLUGIN_CONST:$41\r\n<END>\r\n");

		(IReadOnlyList<PluginScript> scripts, IReadOnlyList<LogEntry> entries, DiagnosticSink diagnostics) = Discover(workspace.RootPath);

		PluginScript script = scripts.Single();
		Assert.AreEqual(workspace.PathOf("Plugin_X.script"), script.Path);

		string note =
			$"The plugin script {workspace.PathOf(Path.Combine("Plugin_X", "Plugin_X.script"))} differs from {script.Path}, which is read instead";

		Assert.ContainsSingle(entry => entry.Severity == LogSeverity.Verbose && entry.Message == note, entries);
		// Two copies of a plugin are the installer's layout, so they do not warn.
		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);
	}

	[TestMethod]
	public void SubfolderCopyThatCannotBeRead_GetsAVerboseNoteThatSaysSo()
	{
		string copy = Path.Combine("Plugin_X", "Plugin_X.script");

		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithTextFile("Plugin_X.script", Script).WithTextFile(copy, Script);

		(IReadOnlyList<PluginScript> scripts, IReadOnlyList<LogEntry> entries, DiagnosticSink diagnostics) = workspace.WithFileLocked(
			copy,
			() => Discover(workspace.RootPath)
		);

		Assert.AreEqual(workspace.PathOf("Plugin_X.script"), scripts.Single().Path);

		string note =
			$"The plugin script {workspace.PathOf(copy)} cannot be compared with {scripts.Single().Path}, which is read instead, "
			+ "because one of the two cannot be read";

		Assert.ContainsSingle(entry => entry.Severity == LogSeverity.Verbose && entry.Message == note, entries);
		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);
	}

	// The test denies the folder listing with a Windows access rule, which no other system has.
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	[SupportedOSPlatform("windows")]
	public void UnreadableSubfolder_WarnsAndIsSkipped()
	{
		using TestWorkspace workspace = TestWorkspace
			.CreateEmpty()
			.WithTextFile(Path.Combine("Locked", "Plugin_Locked.script"), Script)
			.WithTextFile(Path.Combine("Open", "Plugin_Open.script"), Script);

		(IReadOnlyList<PluginScript> scripts, _, DiagnosticSink diagnostics) = workspace.WithListingDenied(
			"Locked",
			() => Discover(workspace.RootPath)
		);

		Assert.AreEqual("Plugin_Open", scripts.Single().Name);

		// The message ends in the operating system's message, which the warning keeps in its exception.
		CompilerDiagnostic warning = diagnostics.Recorded.Single();
		Assert.IsInstanceOfType<UnauthorizedAccessException>(warning.Exception);

		string folder = workspace.PathOf("Locked");

		Assert.IsFalse(warning.IsError, "A folder discovery cannot read is no error of the compile.");

		Assert.AreEqual(
			$"The plugin folder {folder} cannot be read, so no plugin script is read from it: {warning.Exception.Reason()}",
			warning.Message
		);
	}

	// The test denies the folder listing with a Windows access rule, which no other system has.
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	[SupportedOSPlatform("windows")]
	public void UnreadablePluginFolder_WarnsAndReadsNoScript()
	{
		using var workspace = TestWorkspace.CreateEmpty();

		string folder = workspace.PathOf("NG_Center");
		Directory.CreateDirectory(folder);

		(IReadOnlyList<PluginScript> scripts, _, DiagnosticSink diagnostics) = workspace.WithListingDenied("NG_Center", () => Discover(folder));

		Assert.IsEmpty(scripts);

		CompilerDiagnostic warning = diagnostics.Recorded.Single();
		Assert.IsInstanceOfType<UnauthorizedAccessException>(warning.Exception);

		Assert.IsFalse(warning.IsError, "An unreadable plugin folder leaves the plugin constants unknown, which the commands that use them report.");

		Assert.AreEqual(
			$"The plugin folder {folder} cannot be read, so no plugin script is read from it: {warning.Exception.Reason()}",
			warning.Message
		);
	}

	[TestMethod]
	public void PluginScriptThatAnotherProcessLocks_WarnsNamingTheFile()
	{
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithTextFile("Plugin_X.script", Script);
		string path = workspace.PathOf("Plugin_X.script");

		(IReadOnlyList<PluginScript> scripts, _, DiagnosticSink diagnostics) = workspace.WithFileLocked(
			"Plugin_X.script",
			() => Discover(workspace.RootPath)
		);

		Assert.IsEmpty(scripts);

		// The message ends in the operating system's message, which the warning keeps in its exception.
		CompilerDiagnostic warning = diagnostics.Recorded.Single();
		Assert.IsInstanceOfType<IOException>(warning.Exception);

		DiagnosticAssert.HasWarning(
			diagnostics: diagnostics.Recorded,
			line: null,
			message: $"The plugin script Plugin_X.script cannot be read, so its constants are unknown: {warning.Exception.Reason()}",
			file: path
		);
	}

	[TestMethod]
	public void RelativeEngineFolder_GivesAPluginDiagnosticItsFullPath()
	{
		using TestWorkspace workspace = TestWorkspace
			.CreateCompilable()
			.WithPluginFile("Plugin_X.script", "<START_CONSTANTS>\r\n1BAD:$40\r\n<END>\r\n");

		string relative = Path.GetRelativePath(Environment.CurrentDirectory, workspace.RootPath);

		Assert.IsFalse(Path.IsPathRooted(relative), "The engine folder must be on the drive of the test, so that a relative path reaches it.");
		Assert.AreEqual(workspace.RootPath, TRNGCompilerOptions.ResolvePluginDirectory(relative));

		TRNGCompilerOptions options = workspace.CreateOptions() with { EngineDirectory = relative, PluginDirectory = relative };

		AnalysisResult result = workspace.Analyze(options);

		DiagnosticAssert.HasWarning(
			diagnostics: result.Diagnostics,
			line: 2,
			message: "The constant name '1BAD' is skipped, because a name starts with a letter or _ and holds no space",
			file: workspace.PathOf("Plugin_X.script")
		);
	}

	[TestMethod]
	public void DefaultPluginFolder_IsNGCenterWhenItExists()
	{
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithTextFile("Objects.h", string.Empty);

		Assert.AreEqual(workspace.RootPath, TRNGCompilerOptions.ResolvePluginDirectory(workspace.RootPath));

		Directory.CreateDirectory(workspace.PathOf("NG_Center"));

		Assert.AreEqual(workspace.PathOf("NG_Center"), TRNGCompilerOptions.ResolvePluginDirectory(workspace.RootPath));
		Assert.AreEqual(workspace.PathOf("NG_Center"), TRNGCompilerOptions.CreateDefaults(workspace.RootPath).PluginDirectory);
	}
}
