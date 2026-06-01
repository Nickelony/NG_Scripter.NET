using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Kernel.Runtime;

[TestClass]
public sealed class VirtualFileSystemTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void VfsOverridesScriptTxt_DiagnosticsReferenceInMemoryContent()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile("[Strings]\nVFS Override Level\n");

		// The overlay's Script.txt names the level "VFS Override Level", and the one on disk names it "Test Level".
		var overlay = new VirtualFileSystem(
			new Dictionary<string, string>
			{
				[Path.Combine(workspace.ScriptDirectory, "Script.txt")] =
					TestScripts.Language
					+ "[Options]\nTitle= DISABLED\n[Level]\nName= VFS Override Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n",
			}
		);

		AnalysisResult result = workspace.Analyze(overlay: overlay);

		Assert.IsTrue(result.Success, $"Analysis should succeed: {result.Log}");
		Assert.IsNotNull(result.ScriptModel);

		ParsedSection? levelSection = result.ScriptModel.Parsed.Sections.FirstOrDefault(s => s.SectionType == ScriptSectionType.Level);

		Assert.IsNotNull(levelSection, "Should have a Level section from VFS content.");
		Assert.AreEqual("VFS Override Level", levelSection.LevelName, "Level name should come from VFS override, not disk.");

		// English.txt is not in the overlay, so it is read from disk.
		Assert.IsNotNull(result.FirstLanguageData);
		Assert.AreEqual(1, result.FirstLanguageData.TotalSectionStrings, "Language file should still be read from disk when not in VFS.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OverlayHoldingTheTextOfTheDiskFiles_GivesTheAnalysisOfNoOverlay()
	{
		// The script has no Objects.h, so both analyses carry a warning to compare.
		using var workspace = TestWorkspace.CreateCompilable();

		string languageFile = Path.Combine(workspace.ScriptDirectory, "English.txt");

		var overlay = new VirtualFileSystem(
			new Dictionary<string, string>
			{
				[workspace.ScriptFile] = File.ReadAllText(workspace.ScriptFile),
				[languageFile] = File.ReadAllText(languageFile),
			}
		);

		AnalysisResult withoutOverlay = workspace.Analyze(overlay: null);
		AnalysisResult withOverlay = workspace.Analyze(overlay: overlay);

		Assert.IsTrue(withoutOverlay.Success, withoutOverlay.Log);
		Assert.IsNotEmpty(withoutOverlay.Diagnostics);
		Assert.AreEqual(withoutOverlay.Success, withOverlay.Success);
		Assert.AreSequenceEqual(withoutOverlay.Diagnostics, withOverlay.Diagnostics);
		Assert.AreEqual(withoutOverlay.Log, withOverlay.Log);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void VfsOverridesLanguageFile_InMemoryContentIsParsed()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		var overlay = new VirtualFileSystem(
			new Dictionary<string, string> { [Path.Combine(workspace.ScriptDirectory, "English.txt")] = "[Strings]\nOverridden String\nTest Level\n" }
		);

		AnalysisResult result = workspace.Analyze(overlay: overlay);

		Assert.IsTrue(result.Success, $"Analysis should succeed: {result.Log}");
		Assert.IsNotNull(result.FirstLanguageData);
		Assert.AreEqual(2, result.FirstLanguageData.TotalSectionStrings, "Language data should reflect the overridden in-memory content.");
		Assert.AreEqual("Overridden String", result.FirstLanguageData.Strings[0]);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TryGetFile_FindsTheFileItWasBuiltWith()
	{
		string path = Path.Combine(TestWorkspace.TempRoot, "test_script.txt");

		var overlay = new VirtualFileSystem(new Dictionary<string, string> { [path] = "Hello World" });

		Assert.IsTrue(overlay.HasFile(path));
		Assert.IsTrue(overlay.TryGetFile(path, out string? content));
		Assert.AreEqual("Hello World", content);
		Assert.IsFalse(overlay.HasFile(Path.Combine(TestWorkspace.TempRoot, "other.txt")));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Lookup_IgnoresCase()
	{
		var overlay = new VirtualFileSystem(new Dictionary<string, string> { [@"C:\Foo\Bar\Script.txt"] = "content" });

		Assert.IsTrue(overlay.HasFile(@"c:\foo\bar\SCRIPT.TXT"));
		Assert.IsTrue(overlay.TryGetFile(@"C:\FOO\BAR\script.txt", out string? content));
		Assert.AreEqual("content", content);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Keys_AreNormalizedThroughGetFullPath()
	{
		// The folder is a full path of the running system, because a drive letter names no folder on Linux.
		string trle = Path.Combine(TestWorkspace.TempRoot, "TRLE");
		string relative = Path.Combine("vfs-relative", "Script.txt");

		var overlay = new VirtualFileSystem(
			new Dictionary<string, string> { [trle + "/script/../script/Script.txt"] = "slashes", [relative] = "relative" }
		);

		Assert.IsTrue(overlay.TryGetFile(Path.Combine(trle, "script", "Script.txt"), out string? slashes));
		Assert.AreEqual("slashes", slashes);
		Assert.IsTrue(overlay.TryGetFile(Path.GetFullPath(relative), out string? fromRelative));
		Assert.AreEqual("relative", fromRelative);
		Assert.IsTrue(overlay.HasDirectory(Path.Combine(trle, "script") + Path.DirectorySeparatorChar));
		Assert.IsTrue(overlay.HasDirectory(trle));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Constructor_CopiesTheDictionary()
	{
		var files = new Dictionary<string, string> { [@"C:\TRLE\script\Script.txt"] = "first" };
		var overlay = new VirtualFileSystem(files);

		files[@"C:\TRLE\script\Script.txt"] = "changed";
		files[@"C:\TRLE\script\English.txt"] = "added";

		Assert.IsTrue(overlay.TryGetFile(@"C:\TRLE\script\Script.txt", out string? content));
		Assert.AreEqual("first", content);
		Assert.IsFalse(overlay.HasFile(@"C:\TRLE\script\English.txt"));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Constructor_TwoKeysForOneFile_Throws()
	{
		string trle = Path.Combine(TestWorkspace.TempRoot, "TRLE");

		var files = new Dictionary<string, string>
		{
			[Path.Combine(trle, "script", "Script.txt")] = "first",
			[trle + "/script/../script/Script.txt"] = "second",
		};

		Assert.ThrowsExactly<ArgumentException>(() => new VirtualFileSystem(files));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Analysis_WithEveryTextInputInTheOverlay_ReadsNoDisk()
	{
		// The engine folder does not exist on disk, so every file the analysis reads comes from the overlay: Script.txt, the language file,
		// the constants file, Objects.h, a plugin script and an @file text.
		string root = Path.Combine(TestWorkspace.TempRoot, "in-memory-" + Guid.NewGuid().ToString("N"));
		string constants = TextFileUtilities.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripter_constants.txt"), out _);

		var overlay = new VirtualFileSystem(
			new Dictionary<string, string>
			{
				[Path.Combine(root, "script", "Script.txt")] = TestWorkspace.ExtensionSectionsContent + TestScripts.LanguageAndLevel,
				[Path.Combine(root, "script", "English.txt")] = "[Strings]\nTest Level\n[ExtraNG]\n100: @diary.txt\n",
				[Path.Combine(root, "script", "diary.txt")] = "Diary text",
				[Path.Combine(root, "scripter_constants.txt")] = constants,

				[Path.Combine(root, "Objects.h")] =
					"enum object_types\r\n{\r\n    SLOT_TEST,\r\n};\r\n\r\nenum static_types\r\n{\r\n    STATIC_TEST,\r\n};\r\n",

				[Path.Combine(root, "Plugin_X.script")] = "<START_CONSTANTS>\r\nPLUGIN_X_CONST:$40\r\n<END>\r\n",
			}
		);

		var options = new TRNGCompilerOptions
		{
			EngineDirectory = root,
			ScriptDirectory = Path.Combine(root, "script"),
			PluginDirectory = root,
			ConstantsFilePath = Path.Combine(root, "scripter_constants.txt"),
			MainLanguage = "English",
			DeterministicSeed = TestWorkspace.DefaultDeterministicSeed,
		};

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		var entries = new List<LogEntry>();

		compiler.LogEntryProduced += (_, e) => entries.Add(e.Entry);

		AnalysisResult result = compiler.Analyze(options, overlay);

		Assert.IsTrue(result.Success, result.Log);

		DiagnosticAssert.NoDiagnostics(result.Diagnostics);

		Assert.ContainsSingle(entry => entry.Severity == LogSeverity.Info && entry.Message == "Found 1 plugin file(s)", entries, result.Log);
		Assert.IsNotNull(result.Outputs);
		Assert.IsFalse(Directory.Exists(root), "The analysis must not create the engine folder.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void IncludeWithAPathThatDoesNotNormalize_IsNotFound_WithAnOverlay()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		string script = TestScripts.LanguageAndLevel + "#INCLUDE \"a\0b.txt\"\n";

		var overlay = new VirtualFileSystem(new Dictionary<string, string> { [Path.Combine(workspace.ScriptDirectory, "Script.txt")] = script });

		AnalysisResult result = workspace.Analyze(overlay: overlay);

		Assert.IsFalse(result.Success, result.Log);
		Assert.AreEqual("The include file was not found: a\0b.txt", result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Lookup_PathThatDoesNotNormalize_FindsNothing()
	{
		var overlay = new VirtualFileSystem(new Dictionary<string, string> { [@"C:\TRLE\script\Script.txt"] = "content" });

		Assert.IsFalse(overlay.TryGetFile("a\0b.txt", out _));
		Assert.IsFalse(overlay.HasFile("a\0b.txt"));
		Assert.IsFalse(overlay.HasDirectory("a\0b"));
		Assert.IsEmpty(overlay.EnumerateFiles("a\0b"));
		Assert.IsEmpty(overlay.EnumerateDirectories("a\0b"));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OverlayText_GivesTheLinesOfTheSameTextOnDisk()
	{
		// VB6's Line Input ends a file at its last line terminator (Module1.bas:5150), so a trailing terminator adds no empty line.
		const string content = "[Level]\r\nName= A\n\n";

		using var workspace = TestWorkspace.CreateEmpty();
		string path = workspace.PathOf("Script.txt");

		File.WriteAllText(path, content);
		var overlay = new VirtualFileSystem(new Dictionary<string, string> { [path] = content });

		string[] expected = ["[Level]", "Name= A", string.Empty];

		Assert.AreSequenceEqual(expected, ReadScriptLines(path, overlay: null), "From disk");
		Assert.AreSequenceEqual(expected, ReadScriptLines(path, overlay), "From the overlay");
	}

	private static string[] ReadScriptLines(string path, VirtualFileSystem? overlay)
	{
		var loader = new ScriptSourceLoader(
			run: new CompilationRun(conciseLogging: true),
			scriptPath: path,
			scriptDirectory: Path.GetDirectoryName(path)!,
			overlay
		);

		while (loader.HasPendingFiles && loader.TryReadNext(out _)) { }

		return [.. loader.LoadedFiles.Single().Lines];
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Constructor_PathTooLongToNormalize_ThrowsArgumentExceptionHoldingTheCause()
	{
		string tooLong = Path.Combine(TestWorkspace.TempRoot, new string('a', 40_000));

		try
		{
			Path.GetFullPath(tooLong);
			Assert.Inconclusive("This platform normalizes a path of 40000 characters.");
		}
		catch (PathTooLongException)
		{
			// The premise holds: the path cannot be normalized.
		}

		var files = new Dictionary<string, string> { [tooLong] = string.Empty };

		ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => new VirtualFileSystem(files));

		Assert.IsInstanceOfType<PathTooLongException>(error.InnerException);
	}
}
