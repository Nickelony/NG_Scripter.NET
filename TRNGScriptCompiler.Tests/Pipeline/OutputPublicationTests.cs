using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

[TestClass]
public sealed class OutputPublicationTests
{
	// An open file blocks its move on Windows alone; Linux moves a file whatever holds it open. The open file fails the move of the old
	// Script.dat to its backup, a step that a folder in the place of Script.dat skips.
	[TestMethod]
	[TestCategory("IO")]
	[OSCondition(OperatingSystems.Windows)]
	public void PublicationFailure_LeavesExistingOutputsIntact()
	{
		// VB6 deletes the old outputs before it reads the script and copies script.dat and the main language .dat into the engine folder
		// one after the other (Module1.bas:8829-8836, 8961-8973), so a failed copy leaves the folder with a mix of old and new files.
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.LanguageAndLevel + "Image= 1,4,IF_CRYPTIC,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE\n"
		);

		byte[] image = [.. Enumerable.Range(0, 300).Select(index => (byte)index)];

		workspace.WithBinaryFile(Path.Combine("pix", "image4.bmp"), image);
		workspace.WithBinaryFile("Script.dat", [1, 2, 3]);
		workspace.WithBinaryFile("English.dat", [4, 5, 6]);

		TestCompileResult result;

		// Script.dat moves last; held open without delete sharing, its move fails after @image4.bmp and English.dat moved.
		using (new FileStream(path: workspace.PathOf("Script.dat"), mode: FileMode.Open, access: FileAccess.Read, share: FileShare.Read))
			result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		// The message ends in the operating system's message, which the error keeps in its exception.
		CompilerDiagnostic failure = result.Diagnostics.Single(diagnostic => diagnostic.IsError);
		Assert.IsInstanceOfType<IOException>(failure.Exception);

		DiagnosticAssert.SingleError(
			result.Diagnostics,
			line: null,
			$"Failed setting the old Script.dat aside, so it is not replaced: {failure.Exception.Reason()}"
		);

		Assert.AreSequenceEqual(new byte[] { 1, 2, 3 }, workspace.ReadOutput("Script.dat"), "Script.dat keeps the previous bytes.");
		Assert.AreSequenceEqual(new byte[] { 4, 5, 6 }, workspace.ReadOutput("English.dat"), "English.dat is restored from its backup.");
		Assert.AreSequenceEqual(image, workspace.ReadOutput(Path.Combine("pix", "image4.bmp")), "The image stays unencrypted.");

		Assert.IsFalse(
			File.Exists(workspace.PathOf(Path.Combine("pix", "@image4.bmp"))),
			"The encrypted copy is removed with the other new outputs."
		);

		AssertNoStagedOrBackupFile(workspace);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(true)]
	[DataRow(false)]
	public void LogHandlerThatThrowsOnTheCreatedLine_LeavesEveryNewOutput_AndNoStagedOrBackupFile(bool throwsIOException)
	{
		using var workspace = TestWorkspace.CreateCompilable();

		workspace.WithBinaryFile("Script.dat", [1, 2, 3]);
		workspace.WithBinaryFile("English.dat", [4, 5, 6]);

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();

		compiler.LogEntryProduced += (_, e) =>
		{
			if (e.Entry.Message.StartsWith("Created: ", StringComparison.Ordinal))
			{
				throw throwsIOException
					? new IOException("The host's handler failed")
					: (Exception)new InvalidOperationException("The host's handler failed");
			}
		};

		void Compile() => compiler.Compile(workspace.CreateOptions());

		if (throwsIOException)
			Assert.ThrowsExactly<IOException>(Compile, "The handler's exception reaches the host.");
		else
			Assert.ThrowsExactly<InvalidOperationException>(Compile, "The handler's exception reaches the host.");

		// A second workspace composes the bytes of the same script and seed, which the throwing handler must not change.
		using var reference = TestWorkspace.CreateCompilable();
		Assert.IsTrue(reference.Compile().Success);

		Assert.AreSequenceEqual(reference.ReadScriptDat(), workspace.ReadOutput("Script.dat"), "Script.dat holds the new bytes.");
		Assert.AreSequenceEqual(reference.ReadOutput("English.dat"), workspace.ReadOutput("English.dat"), "English.dat holds the new bytes.");

		AssertNoStagedOrBackupFile(workspace);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LogHandlerThatThrowsOnTheWriteError_StillRestoresThePreviousOutputs()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		Directory.CreateDirectory(workspace.PathOf("Script.dat"));
		workspace.WithBinaryFile("English.dat", [4, 5, 6]);

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();

		compiler.LogEntryProduced += (_, e) =>
		{
			if (e.Entry.Severity is LogSeverity.Error)
				throw new IOException("The host's handler failed");
		};

		// Script.dat moves last; a folder in its place fails the move after English.dat moved, on every system.
		Assert.ThrowsExactly<IOException>(() => compiler.Compile(workspace.CreateOptions()));

		Assert.IsTrue(Directory.Exists(workspace.PathOf("Script.dat")), "The folder in the place of Script.dat stays.");
		Assert.AreSequenceEqual(new byte[] { 4, 5, 6 }, workspace.ReadOutput("English.dat"), "English.dat is restored from its backup.");

		AssertNoStagedOrBackupFile(workspace);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void FailedPublication_EndsTheLogWithTheErrorAndWarningCounts()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		Directory.CreateDirectory(workspace.PathOf("Script.dat"));

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success);

		IReadOnlyList<LogEntry> lastEntries = [.. result.Entries.TakeLast(2)];

		Assert.AreEqual(LogSeverity.Error, lastEntries[0].Severity);
		Assert.AreEqual(LogSeverity.Info, lastEntries[1].Severity);

		int errorCount = result.Diagnostics.Count(diagnostic => diagnostic.IsError);
		int warningCount = result.Diagnostics.Count - errorCount;

		Assert.AreEqual($"{errorCount} error(s) and {warningCount} warning(s) found", lastEntries[1].Message);
	}

	private static void AssertNoStagedOrBackupFile(TestWorkspace workspace)
	{
		Assert.IsEmpty(
			Directory
				.GetFiles(workspace.RootPath, "*.*", SearchOption.AllDirectories)
				.Where(path => path.EndsWith(".tmp", StringComparison.Ordinal) || path.EndsWith(".bak", StringComparison.Ordinal)),

			"No staged or backup file remains."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void SuccessfulCompile_PublishesOutputsAndKeepsADatFileNoScriptNames()
	{
		// PD-23: VB6 deletes every .dat but script.dat in the engine folder (Module1.bas:8829-8836); the port deletes only the
		// language files a Script.dat names, so another tool's .dat stays.
		using var workspace = TestWorkspace.CreateCompilable();
		workspace.WithBinaryFile("Other.dat", [1, 2, 3]);

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		CompileResult first = compiler.Compile(workspace.CreateOptions());

		Assert.IsTrue(first.Success, $"Compile should succeed: {first.Log}");

		// The second compile replaces the outputs of the first, through backups it deletes afterwards.
		CompileResult second = compiler.Compile(workspace.CreateOptions());
		Assert.IsTrue(second.Success, $"Compile should succeed: {second.Log}");

		Assert.AreSequenceEqual(new byte[] { 1, 2, 3 }, workspace.ReadOutput("Other.dat"), "A .dat no Script.dat names is not a stale language.");

		Assert.IsTrue(File.Exists(workspace.PathOf("Script.dat")), "Script.dat must be published.");
		Assert.IsTrue(File.Exists(workspace.PathOf("English.dat")), "English.dat must be published.");
		Assert.IsEmpty(Directory.GetFiles(workspace.RootPath, "*.tmp"), "No staged temp files may remain after a successful compile.");
		Assert.IsEmpty(Directory.GetFiles(workspace.RootPath, "*.bak"), "No backup may remain after a successful compile.");
	}

	// Linux deletes a read-only file whatever its attribute, so only Windows shows the backup the fix removes.
	[TestMethod]
	[TestCategory("IO")]
	[OSCondition(OperatingSystems.Windows)]
	public void ReadOnlyScriptDat_IsReplacedWithoutAWarningOrALeftoverBackup()
	{
		using var workspace = TestWorkspace.CreateClean();

		// The first compile writes the Script.dat that the second one reads and replaces.
		TestCompileResult first = workspace.Compile();
		Assert.IsTrue(first.Success, first.Log);

		string scriptDatPath = workspace.PathOf("Script.dat");
		File.SetAttributes(scriptDatPath, FileAttributes.ReadOnly);

		TestCompileResult second = workspace.Compile();

		Assert.IsTrue(second.Success, second.Log);
		DiagnosticAssert.NoDiagnostics(second.Diagnostics);
		Assert.IsEmpty(Directory.GetFiles(workspace.RootPath, "*.bak"), "No backup remains.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void DuplicateLanguageOutputs_Fail()
	{
		// ENGLISH2.TXT takes the casing of English2.txt on disk, so the two entries name one file on every file system (PD-29).
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "File= 1, English2.txt\nFile= 2, ENGLISH2.TXT\n" + TestScripts.Level
		);

		workspace.WithTextFile(Path.Combine("script", "English2.txt"), TestWorkspace.CompilableLanguageContent);

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		CompileResult result = compiler.Compile(workspace.CreateOptions());

		Assert.IsFalse(result.Success, "Two language files mapping to the same output must fail the compile.");

		// The error points at the file name on the later of the two File= lines.
		CompilerDiagnostic diagnostic = DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 4,
			message: "Multiple outputs map to the same file name 'ENGLISH2.dat'; give each [Language] File= entry a name of its own",
			file: workspace.PathOf(Path.Combine("script", "Script.txt"))
		);

		Assert.AreEqual(("File= 2, ".Length, "ENGLISH2.TXT".Length), (diagnostic.Column, diagnostic.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void DuplicateOfTheFirstLanguageOutput_Fails()
	{
		// VB6 writes one .dat per [Language] entry, named after the entry (Module1.bas:8886-8888, 8927), so the second entry overwrites the first.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "File= 1, ENGLISH.TXT\n" + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 3,
			message: "Multiple outputs map to the same file name 'ENGLISH.dat'; give each [Language] File= entry a name of its own",
			file: workspace.PathOf(Path.Combine("script", "Script.txt"))
		);

		Assert.IsFalse(File.Exists(workspace.PathOf("Script.dat")), "A failed compile publishes nothing.");
	}
}
