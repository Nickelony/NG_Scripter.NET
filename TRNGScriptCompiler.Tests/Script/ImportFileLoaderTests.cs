using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

/// <summary>The loader reads each import from disk, so every test writes its files into a temp workspace.</summary>
[TestClass]
[TestCategory("IO")]
public sealed class ImportFileLoaderTests
{
	private const int Line = 3;

	private sealed class LoaderFixture
	{
		private readonly ImportFileLoader _loader;

		internal LoaderFixture(TestWorkspace workspace)
		{
			Workspace = workspace;

			_loader = new ImportFileLoader(
				run: Unit.Run,
				expressionEvaluator: Unit.Evaluator,
				engineDirectoryPath: workspace.RootPath,
				importFiles: Files
			);
		}

		internal TestWorkspace Workspace { get; }
		internal UnitRun Unit { get; } = new();
		internal List<NgImportFile> Files { get; } = [];
		internal string ScriptFile => Workspace.PathOf(Path.Combine("script", "Script.txt"));

		internal bool Load(string text)
		{
			(string command, List<string> arguments) = ScriptLineLexer.ParseCommandLine(text);
			var location = new SourceLocation(Line, File: ScriptFile);

			return _loader.TryLoad(new ScriptCommandLine(text, SourceText: text, command, arguments, location));
		}
	}

	private static void AssertFails(TestWorkspace workspace, string text, string expectedError)
	{
		var fixture = new LoaderFixture(workspace);

		Assert.IsFalse(fixture.Load(text));
		Assert.IsEmpty(fixture.Files);

		DiagnosticAssert.SingleError(diagnostics: fixture.Unit.Diagnostics, Line, message: expectedError, file: fixture.ScriptFile);
	}

	[TestMethod]
	public void ImportFile_ReadsTheFileBelowTheEngineFolder()
	{
		// VB6 reads ImportFile= as Word:String:Word:Word, prefixes the path with the engine folder and reads the whole file (Module1.bas:7634-7657).
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithBinaryFile(Path.Combine("import", "data1.bin"), [1, 2, 3]);
		var fixture = new LoaderFixture(workspace);

		Assert.IsTrue(fixture.Load(@"ImportFile= $10, import\data1.bin, FTYPE_SETTINGS, IMPORT_MEMORY"));

		NgImportFile file = fixture.Files.Single();

		Assert.AreEqual((16, @"import\data1.bin", 3, NgImportMode.Memory), (file.ImportId, file.FileName, file.FileType, file.ImportMode));
		Assert.AreSequenceEqual(new byte[] { 1, 2, 3 }, file.Data);

		Assert.AreEqual((Line, fixture.ScriptFile), (file.Location.Line, file.Location.File));
		Assert.IsTrue(fixture.Unit.Run.WasFileRead(workspace.PathOf(Path.Combine("import", "data1.bin"))));

		DiagnosticAssert.NoDiagnostics(fixture.Unit.Diagnostics);
	}

	[TestMethod]
	public void PathWithALeadingBackslash_ReadsTheFileBelowTheEngineFolder()
	{
		// VB6 prefixes the engine folder and a '\' to the path as written (Module1.bas:7635), and Windows reads the doubled '\' as one, so a
		// leading '\' does not root the path; the name in Script.dat keeps it (:7645).
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithBinaryFile(Path.Combine("import", "data1.bin"), [1, 2, 3]);
		var fixture = new LoaderFixture(workspace);

		Assert.IsTrue(fixture.Load(@"ImportFile= 1, \import\data1.bin, 1, 1"));

		NgImportFile file = fixture.Files.Single();

		Assert.AreEqual(@"\import\data1.bin", file.FileName);
		Assert.AreSequenceEqual(new byte[] { 1, 2, 3 }, file.Data);
	}

	[TestMethod]
	public void ImportFileOnlyBesideTheScript_IsTheFallback()
	{
		// VB6 looks in the engine folder alone and fails the file there (Module1.bas:7635-7640); the port looks beside the script file next (PD-83).
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithBinaryFile(Path.Combine("script", "data1.bin"), [9]);

		var fixture = new LoaderFixture(workspace);

		Assert.IsTrue(fixture.Load("ImportFile= 1, data1.bin, 1, 2"));
		Assert.AreSequenceEqual("\t"u8.ToArray(), fixture.Files.Single().Data);
		Assert.AreEqual(NgImportMode.Temporary, fixture.Files.Single().ImportMode);
	}

	[TestMethod]
	public void SecondImportOfAnId_Fails()
	{
		// VB6 checks the IDs of every [Options] section as one list (Module1.bas:8306, AzzeraDuplicati at :1212), and an IMPORT argument names
		// its import by ID.
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithBinaryFile("a.bin", [1]).WithBinaryFile("b.bin", [2]);

		var fixture = new LoaderFixture(workspace);

		Assert.IsTrue(fixture.Load("ImportFile= 1, a.bin, 1, 1"));
		Assert.IsFalse(fixture.Load("ImportFile= 1, b.bin, 1, 1"));

		Assert.AreEqual("a.bin", fixture.Files.Single().FileName);

		DiagnosticAssert.SingleError(
			fixture.Unit.Diagnostics,
			Line,
			"ImportFile= ID 1 is already declared for a.bin; give each ImportFile= its own ID"
		);
	}

	[TestMethod]
	public void IdsThatWriteOneWord_AreOneId()
	{
		// The import chunk writes both -1 and 65535 as the word -1, so the second line repeats the first.
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithBinaryFile("a.bin", [1]).WithBinaryFile("b.bin", [2]);

		var fixture = new LoaderFixture(workspace);

		Assert.IsTrue(fixture.Load("ImportFile= -1, a.bin, 1, 1"));
		Assert.IsFalse(fixture.Load("ImportFile= 65535, b.bin, 1, 1"));

		DiagnosticAssert.SingleError(
			fixture.Unit.Diagnostics,
			Line,
			"ImportFile= ID 65535 is the same word as ID -1, already declared for a.bin; give each ImportFile= its own ID"
		);
	}

	[TestMethod]
	public void IdsMinusOneAndZero_AreTwoIds()
	{
		// VB6 reads a repeated ID of -1 as 0 (Module1.bas:6883), but the chunks write two words (PD-95).
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithBinaryFile("a.bin", [1]).WithBinaryFile("b.bin", [2]);

		var fixture = new LoaderFixture(workspace);

		Assert.IsTrue(fixture.Load("ImportFile= -1, a.bin, 1, 1"));
		Assert.IsTrue(fixture.Load("ImportFile= 0, b.bin, 1, 1"));

		DiagnosticAssert.NoDiagnostics(fixture.Unit.Diagnostics);
		Assert.AreSequenceEqual([-1, 0], fixture.Files.Select(file => file.ImportId));
	}

	[TestMethod]
	public void MissingFile_Fails()
	{
		// VB6 fails a file the engine folder lacks (Module1.bas:7635-7640).
		using var workspace = TestWorkspace.CreateEmpty();
		AssertFails(workspace, "ImportFile= 1, missing.bin, 1, 1", "The import file was not found: missing.bin");
	}

	[TestMethod]
	public void AbsolutePath_Fails()
	{
		// VB6 prefixes the engine folder to the path as written (Module1.bas:7635), so a path with a drive never resolves (PD-83).
		using var workspace = TestWorkspace.CreateEmpty();

		AssertFails(
			workspace,
			@"ImportFile= 1, C:\data1.bin, 1, 1",
			$@"The ImportFile= path 'C:\data1.bin' is absolute, but an import path is relative to the engine folder; write it relative to {workspace.RootPath}"
		);
	}

	[TestMethod]
	public void PathOf80Characters_Fails()
	{
		// The name field holds 80 bytes with its terminator, and VB6 cuts a longer name without one (Module1.bas:7645) (PD-56).
		using var workspace = TestWorkspace.CreateEmpty();
		string path = new string('a', 76) + ".bin";

		AssertFails(
			workspace,
			$"ImportFile= 1, {path}, 1, 1",
			$"The ImportFile= path '{path}' is 80 characters long, but its field holds 79 and a terminator; move the file to a shorter path"
		);
	}

	[TestMethod]
	[DataRow("ImportFile= 1, a.bin, 1", "Wrong number of arguments for 'ImportFile=': expected 4 (ID, Path, Type, Mode), got 3")]
	[DataRow("ImportFile= 65536, a.bin, 1, 1", "The ImportFile= ID 65536 lies outside -32768 to 65535, the values its word holds")]
	[DataRow("ImportFile= 1, a.bin, -32769, 1", "The ImportFile= file type -32769 lies outside -32768 to 65535, the values its word holds")]
	[DataRow("ImportFile= 1, a.bin, 1, 3", "The ImportFile= mode 3 lies outside 0 to 2; write 0, IMPORT_MEMORY or IMPORT_TEMPORARY")]
	[DataRow("ImportFile= ONE, a.bin, 1, 1", "Invalid Import ID: ONE is neither a number nor a known name")]
	public void InvalidArgument_Fails(string text, string expectedError)
	{
		// VB6 reads ImportFile= as an NG command of four arguments (Module1.bas:7071-7099), width-checks each word (:4915-4937), and names the
		// mode through a three-entry array (:8282, 8600-8602); a word that fits only by wrapping fails in the port (PD-56).
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithBinaryFile("a.bin", [1]);
		AssertFails(workspace, text, expectedError);
	}
}
