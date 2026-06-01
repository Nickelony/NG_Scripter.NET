using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Kernel.Runtime;

/// <summary>
/// Covers the options surface: fail-fast validation, equality, the nullable seed and the guessed-path notes.
/// </summary>
/// <remarks>
/// The notes are those of
/// <see cref="TRNGCompilerOptions.CreateDefaults(string, MainLanguageSelector?, out IReadOnlyList{CompilerDiagnostic})"/>.
/// </remarks>
[TestClass]
public sealed class TRNGCompilerOptionsTests
{
	private static TRNGCompilerOptions CreateOptionsFor(string root, string? constantsPath = null)
	{
		return new()
		{
			EngineDirectory = root,
			ScriptDirectory = Path.Combine(root, "script"),
			PluginDirectory = root,
			ConstantsFilePath = constantsPath ?? Path.Combine(root, "scripter_constants.txt"),
		};
	}

	private static TRNGCompiler CompilerAt(double timerSeconds) => new() { ConciseLogging = true, TimerSeconds = () => timerSeconds };

	[TestMethod]
	[TestCategory("Fast")]
	public void Validate_MissingRequiredPaths_ReportsErrors()
	{
		string missingRoot = Path.Combine(TestWorkspace.TempRoot, "trng-missing-" + Guid.NewGuid().ToString("N"));
		TRNGCompilerOptions options = CreateOptionsFor(missingRoot);
		IReadOnlyList<CompilerDiagnostic> diagnostics = options.Validate();

		// A missing plugin folder is a warning, because the run then discovers no plugins.
		Assert.AreSequenceEqual(
			new (bool IsError, int? Line, string Message)[]
			{
				(true, null, $"Engine folder was not found: {missingRoot}"),
				(true, null, $"Script folder was not found: {options.ScriptDirectory}"),
				(false, null, $"Plugin folder was not found: {missingRoot}"),
				(true, null, $"Constants file was not found: {options.ConstantsFilePath}"),
			},

			diagnostics.Select(diagnostic => (diagnostic.IsError, diagnostic.LineNumber, diagnostic.Message)).ToArray()
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Validate_BlankPath_ReportsErrorWithoutThrowing()
	{
		TRNGCompilerOptions options = CreateOptionsFor(TestWorkspace.TempRoot) with { EngineDirectory = "  " };
		IReadOnlyList<CompilerDiagnostic> diagnostics = options.Validate();

		Assert.Contains(
			diagnostic => diagnostic.IsError && diagnostic.Message == "Engine folder path is not set",
			diagnostics,
			"A blank path must be reported as fatal with a dedicated message."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Validate_ExistingPaths_ReturnsNoDiagnostics()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		TRNGCompilerOptions options = workspace.CreateOptions();

		Assert.IsEmpty(options.Validate(), "A fully-populated workspace must validate cleanly.");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void MainLanguage_AcceptsTextAndIndex()
	{
		TRNGCompilerOptions options = CreateOptionsFor(TestWorkspace.TempRoot);

		Assert.AreEqual("English", (options with { MainLanguage = "English" }).MainLanguage?.Text);
		Assert.AreEqual(1, (options with { MainLanguage = 1 }).MainLanguage?.Index);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Options_BuiltFromTheSameText_AreEqual()
	{
		TRNGCompilerOptions options = CreateOptionsFor(TestWorkspace.TempRoot);

		Assert.AreEqual(options with { MainLanguage = "English" }, options with { MainLanguage = "English" });
		Assert.AreEqual(options with { MainLanguage = 1 }, options with { MainLanguage = "1" });
		Assert.AreNotEqual(options with { MainLanguage = "English" }, options with { MainLanguage = "French" });
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ResolveScriptDirectory_EngineWithTrailingSeparator_FindsSiblingScriptFolder()
	{
		// A TRLE layout keeps the script folder beside the engine folder, as in C:\TRLE\Engine and C:\TRLE\script.
		using var workspace = TestWorkspace.CreateEmpty();
		string engine = workspace.PathOf("Engine");
		Directory.CreateDirectory(engine);

		string resolved = TRNGCompilerOptions.ResolveScriptDirectory(engine + Path.DirectorySeparatorChar);

		Assert.AreEqual(workspace.ScriptDirectory, resolved);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(".")]
	[DataRow("sub/..")]
	public void ResolveScriptDirectory_EngineThatEndsInADotSegment_FindsSiblingScriptFolder(string suffix)
	{
		// A TombIDE Engine folder passed as "." has an empty parent until the path is made full.
		using var workspace = TestWorkspace.CreateEmpty();
		string engine = workspace.PathOf("Engine");
		Directory.CreateDirectory(Path.Combine(engine, "sub"));

		string resolved = TRNGCompilerOptions.ResolveScriptDirectory(Path.Combine(engine, suffix));

		Assert.AreEqual(workspace.ScriptDirectory, resolved);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void PublicFactories_NullArgument_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TRNGCompilerOptions.ResolveScriptDirectory(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => TRNGCompilerOptions.ResolvePluginDirectory(null!));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void DeterministicSeedOfZero_WritesTheSameScriptDatEveryTime()
	{
		// VB6 reseeds from the timer before every draw of the NG security padding (MioModulo.bas:855-856). The instrumented build that made
		// the goldens pins a seed but reads 0 as none (:850); the test-only option takes 0 as a seed like any other.
		using var first = TestWorkspace.CreateCompilable();
		using var second = TestWorkspace.CreateCompilable();
		using var otherSeed = TestWorkspace.CreateCompilable();

		Assert.IsTrue(first.Compile(first.CreateOptions(deterministicSeed: 0)).Success);
		Assert.IsTrue(second.Compile(second.CreateOptions(deterministicSeed: 0)).Success);
		Assert.IsTrue(otherSeed.Compile(otherSeed.CreateOptions(deterministicSeed: 1)).Success);

		Assert.AreSequenceEqual(first.ReadScriptDat(), second.ReadScriptDat(), "A seed of 0 pins the padding like any other seed.");
		Assert.AreNotSequenceEqual(first.ReadScriptDat(), otherSeed.ReadScriptDat(), "The padding depends on the seed.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void UnseededCompile_TakesTheSecurityPaddingFromTheTimer()
	{
		// Pristine VB6 seeds the padding from Timer (PD-07). The compiler's timer seam fixes the clock, so the bytes compare without a seed.
		using var first = TestWorkspace.CreateCompilable();
		using var second = TestWorkspace.CreateCompilable();
		using var otherTime = TestWorkspace.CreateCompilable();

		TestCompileResult firstResult = first.Compile(TRNGCompilerOptions.CreateDefaults(first.RootPath, "English"), CompilerAt(40000.25));
		TestCompileResult secondResult = second.Compile(TRNGCompilerOptions.CreateDefaults(second.RootPath, "English"), CompilerAt(40000.25));
		TestCompileResult otherResult = otherTime.Compile(TRNGCompilerOptions.CreateDefaults(otherTime.RootPath, "English"), CompilerAt(61234.5));

		Assert.IsTrue(firstResult.Success, firstResult.Log);
		Assert.IsTrue(secondResult.Success, secondResult.Log);
		Assert.IsTrue(otherResult.Success, otherResult.Log);

		Assert.AreSequenceEqual(first.ReadScriptDat(), second.ReadScriptDat(), "One timer value writes one padding.");
		Assert.AreNotSequenceEqual(first.ReadScriptDat(), otherTime.ReadScriptDat(), "The padding follows the timer.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void CreateDefaults_GuessedWorkspace_ReportsNote()
	{
		string engineRoot = Path.Combine(TestWorkspace.TempRoot, "trng-guess-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(engineRoot);

		try
		{
			var options = TRNGCompilerOptions.CreateDefaults(engineRoot, mainLanguage: null, out IReadOnlyList<CompilerDiagnostic>? notes);

			// When neither script candidate exists, CreateDefaults reports the guess.
			DiagnosticAssert.HasWarning(
				notes,
				line: null,
				$"Script folder was guessed because neither 'script' candidate exists: {Path.Combine(engineRoot, "script")}"
			);

			Assert.AreEqual(
				Path.Combine(engineRoot, "script"),
				options.ScriptDirectory,
				"The guessed script folder is the primary candidate (the path may not exist on disk)."
			);
		}
		finally
		{
			Directory.Delete(engineRoot, recursive: true);
		}
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Validate_ConstantsFileThatIsAFolder_ReportsAnError()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		TRNGCompilerOptions options = workspace.CreateOptions() with { ConstantsFilePath = workspace.ScriptDirectory };

		Assert.AreSequenceEqual(
			new (bool IsError, string Message)[] { (true, $"Constants file is a folder, not a file: {workspace.ScriptDirectory}") },
			options.Validate().Select(diagnostic => (diagnostic.IsError, diagnostic.Message)).ToArray()
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Validate_FolderOptionsThatAreFiles_ReportAnErrorEach()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		string file = workspace.CreateOptions().ConstantsFilePath;

		TRNGCompilerOptions options = workspace.CreateOptions() with { EngineDirectory = file, ScriptDirectory = file, PluginDirectory = file };

		Assert.AreSequenceEqual(
			new (bool IsError, string Message)[]
			{
				(true, $"Engine folder is a file, not a folder: {file}"),
				(true, $"Script folder is a file, not a folder: {file}"),
				(true, $"Plugin folder is a file, not a folder: {file}"),
			},

			options.Validate().Select(diagnostic => (diagnostic.IsError, diagnostic.Message)).ToArray()
		);
	}
}
