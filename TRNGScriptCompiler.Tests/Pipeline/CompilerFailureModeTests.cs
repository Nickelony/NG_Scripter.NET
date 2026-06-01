using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

/// <summary>
/// Failure modes of the full pipeline: missing inputs, write-phase I/O failures, and two-run idempotency
/// (including the VB6 "already encrypted" image branch).
/// </summary>
[TestClass]
public sealed class CompilerFailureModeTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void Compile_MissingScriptFile_Fails()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		workspace.WithObjectsH();

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "A workspace without Script.txt must fail.");
		DiagnosticAssert.SingleError(result.Diagnostics, line: null, $"Cannot find Script.txt in: {workspace.CreateOptions().ScriptDirectory}");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Compile_MissingScriptFolder_Fails()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		TRNGCompilerOptions options = workspace.CreateOptions() with { ScriptDirectory = workspace.PathOf("missing-script-directory") };
		TestCompileResult result = workspace.Compile(options);

		Assert.IsFalse(result.Success, "A missing script folder must fail.");
		DiagnosticAssert.SingleError(result.Diagnostics, line: null, $"Script folder was not found: {options.ScriptDirectory}");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Compile_EngineDirectoryBecomesAFileAfterTheAnalysis_WritePhaseFails()
	{
		using var workspace = TestWorkspace.CreateCompilable();

		// The validation rejects an engine folder that is a file, so the folder turns into a file after the analysis: the banner of
		// the publication is the first line after it. The write phase cannot create Script.dat below the file.
		string engine = workspace.PathOf("engine");
		Directory.CreateDirectory(engine);

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();

		compiler.LogEntryProduced += (_, e) =>
		{
			if (e.Entry.Message == "**** START COMPILING ****")
			{
				Directory.Delete(engine);
				File.WriteAllText(engine, "not a directory");
			}
		};

		CompileResult result = compiler.Compile(workspace.CreateOptions() with { EngineDirectory = engine });

		Assert.IsFalse(result.Success, "The write phase must fail when the engine folder is a file.");

		// The message ends in the operating system's message, which the error keeps in its exception.
		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);

		Assert.IsNotNull(diagnostic.Exception, "The diagnostic keeps the I/O exception as evidence.");
		DiagnosticAssert.SingleError(result.Diagnostics, line: null, $"Failed staging English.dat: {diagnostic.Exception.Reason()}");
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("scripter_constants.txt", "Cannot read the constants file: ", null)]
	[DataRow("script\\English.txt", "Cannot read language file English.txt: ", null)]
	[DataRow("data.bin", "Cannot read import file data.bin: ", 4)]
	public void Compile_InputFileLockedAgainstReading_FailsWithTheIOExceptionAttached(string relativePath, string expectedPrefix, int? expectedLine)
	{
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nImportFile= 1,data.bin,FTYPE_USERFILE,IMPORT_MEMORY\n" + TestScripts.Level
		);

		workspace.WithBinaryFile("data.bin", [0xA1, 0xA2, 0xA3, 0xA4]);

		TRNGCompilerOptions options = workspace.CreateOptions();
		string constantsPath = workspace.PathOf("scripter_constants.txt");

		File.Copy(options.ConstantsFilePath, constantsPath);
		options = options with { ConstantsFilePath = constantsPath };

		TestCompileResult result = workspace.WithFileLocked(relativePath, () => workspace.Compile(options));

		Assert.IsFalse(result.Success, result.Log);

		// The message ends in the operating system's message, which the error keeps in its exception.
		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);

		Assert.IsInstanceOfType<IOException>(diagnostic.Exception, "The diagnostic keeps the I/O exception as evidence.");
		DiagnosticAssert.SingleError(result.Diagnostics, expectedLine, expectedPrefix + diagnostic.Exception.Reason());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Compile_TwoRuns_AreIdempotent_AndImageEncryptionIsSkippedTheSecondTime()
	{
		using var workspace = TestWorkspace.Create(TestScripts.ImageLevel + TestScripts.CrypticImage);
		workspace.WithLanguageFile("English.txt", "[Strings]\nImage Level\n");

		byte[] sourceBytes = [.. Enumerable.Range(0, 300).Select(index => (byte)(index % 256))];
		workspace.WithBinaryFile(Path.Combine("pix", "image4.bmp"), sourceBytes);

		TRNGCompilerOptions options = workspace.CreateOptions();

		CompileResult first = TestWorkspace.CreateCompiler().Compile(options);
		Assert.IsTrue(first.Success, first.Log);

		byte[] firstScriptDat = workspace.ReadScriptDat();
		byte[] firstEnglishDat = workspace.ReadOutput("English.dat");
		byte[] encryptedBytes = File.ReadAllBytes(workspace.PathOf(Path.Combine("pix", "@image4.bmp")));

		Assert.IsFalse(File.Exists(workspace.PathOf(Path.Combine("pix", "image4.bmp"))), "The source image must be consumed by the first run.");

		// The second run uses verbose logging so the "already encrypted" note is observable.
		TestCompileResult second = workspace.Compile(options, new TRNGCompiler { ConciseLogging = false });
		Assert.IsTrue(second.Success, second.Log);

		Assert.AreSequenceEqual(
			firstScriptDat,
			workspace.ReadScriptDat(),
			"A second compile with the same deterministic seed must produce identical Script.dat bytes."
		);

		Assert.AreSequenceEqual(firstEnglishDat, workspace.ReadOutput("English.dat"), "A second compile must produce identical language bytes.");

		Assert.AreSequenceEqual(
			encryptedBytes,
			File.ReadAllBytes(workspace.PathOf(Path.Combine("pix", "@image4.bmp"))),
			"An already-encrypted image must not be re-encrypted."
		);

		Assert.ContainsSingle(
			entry => entry.Severity == LogSeverity.Verbose && entry.Message == "image4.bmp has been already encrypted",
			second.Entries,
			"The second run must take the VB6 'already encrypted' branch instead of failing on the missing source."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Analyze_OnUncompilableWorkspace_ReturnsNoWriteArtifacts()
	{
		// Analysis-only runs must never write .dat files, even for workspaces that fail analysis.
		// English.txt is referenced but intentionally absent, so the language phase fails.
		using var workspace = TestWorkspace.Create(
			TestScripts.Language + "[Level]\nName= Test Level\nLevel= data\\broken,0\nLoadCamera= 0,0,0,0,0,0,0\n"
		);

		AnalysisResult result = workspace.Analyze();

		Assert.IsFalse(result.Success, "The workspace must fail analysis (the language file is missing).");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 2,
			message: "Missing language file: English.txt",
			file: Path.Combine(workspace.ScriptDirectory, "Script.txt")
		);

		Assert.IsEmpty(Directory.GetFiles(workspace.RootPath, "*.dat"), "Analysis must not produce .dat artifacts.");
	}
}
