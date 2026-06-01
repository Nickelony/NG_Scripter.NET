using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

[TestClass]
public sealed class TRNGCompilerTests
{
	[TestMethod]
	[TestCategory("Fast")]
	public void Analyze_NullOptions_Throws()
	{
		var compiler = new TRNGCompiler();
		Assert.ThrowsExactly<ArgumentNullException>(() => compiler.Analyze(null!));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Compile_NullOptions_Throws()
	{
		var compiler = new TRNGCompiler();
		Assert.ThrowsExactly<ArgumentNullException>(() => compiler.Compile(null!));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Compile_ReturnsThePublishedFiles_WithScriptDatLast()
	{
		using var workspace = TestWorkspace.CreateCompilable();

		CompileResult result = TestWorkspace.CreateCompiler().Compile(workspace.CreateOptions());

		Assert.IsTrue(result.Success, result.Log);
		Assert.HasCount(2, result.PublishedFiles, string.Join(" | ", result.PublishedFiles));
		Assert.AreEqual(workspace.PathOf("English.dat"), result.PublishedFiles[0], ignoreCase: true);
		Assert.AreEqual(workspace.PathOf("Script.dat"), result.PublishedFiles[1], ignoreCase: true);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Compile_FailedAnalysis_PublishesNothing_AndReportsTheError()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Level]\nName= Test Level\nLevel= data\\test,0\n");

		CompileResult result = TestWorkspace.CreateCompiler().Compile(workspace.CreateOptions());

		Assert.IsFalse(result.Success, result.Log);
		Assert.IsEmpty(result.PublishedFiles);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 3,
			message: "LoadCamera= command is required in level section 'Test Level'",
			file: workspace.ScriptFile
		);

		Assert.IsFalse(File.Exists(workspace.PathOf("Script.dat")), "A failed compile writes no Script.dat.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Compile_LogsTheCompileBannerAfterTheAnalysis_AndOnlyWhenItPublishes()
	{
		// VB6 logs the banner before it reads the script (Module1.bas:8818); the port logs it where publication starts (PD-93).
		using var workspace = TestWorkspace.CreateCompilable();
		using var failingWorkspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Level]\nName= Test Level\nLevel= data\\test,0\n");

		string[] published = BannerLines(workspace);
		string[] failed = BannerLines(failingWorkspace);

		Assert.AreSequenceEqual(
			new[] { "**** START ANALYZING ****", "******** ANALYSIS COMPLETED ********", "**** START COMPILING ****", "******** COMPLETED ********" },
			published
		);

		Assert.AreSequenceEqual(
			new[] { "**** START ANALYZING ****", "******** ANALYSIS FAILED ********" },
			failed,
			"A failed analysis announces no compile."
		);

		static string[] BannerLines(TestWorkspace workspace)
		{
			TRNGCompiler compiler = TestWorkspace.CreateCompiler();
			var lines = new List<string>();

			compiler.LogEntryProduced += (_, e) => lines.Add(e.Entry.FormattedMessage);
			compiler.Compile(workspace.CreateOptions());

			return [.. lines.Where(line => line.StartsWith("****", StringComparison.Ordinal))];
		}
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LogEntryProduced_StreamsTheLogText_WithTheCompilerAsSender()
	{
		using var workspace = TestWorkspace.CreateCompilable();

		var compiler = new TRNGCompiler { ConciseLogging = false };
		var lines = new List<string>();
		var senders = new HashSet<object?>();

		compiler.LogEntryProduced += (sender, e) =>
		{
			senders.Add(sender);
			lines.Add(e.Entry.FormattedMessage);
		};

		CompileResult result = compiler.Compile(workspace.CreateOptions());

		Assert.IsTrue(result.Success, result.Log);
		Assert.AreEqual(result.Log, string.Join(Environment.NewLine, lines), "The streamed lines are the log text, separators included.");
		Assert.AreSame(compiler, senders.Single());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LogEntryProduced_CarriesAnErrorWithItsSeverityAndMessage()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Level]\nName= Test Level\nLevel= data\\test,0\n");

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		var errors = new List<LogEntry>();

		compiler.LogEntryProduced += (_, e) =>
		{
			if (e.Entry.Severity is LogSeverity.Error)
				errors.Add(e.Entry);
		};

		AnalysisResult result = compiler.Analyze(workspace.CreateOptions());

		CompilerDiagnostic fatal = DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 3,
			message: "LoadCamera= command is required in level section 'Test Level'",
			file: workspace.ScriptFile
		);

		LogEntry entry = errors.Single();

		Assert.AreEqual(LogSeverity.Error, entry.Severity);
		Assert.AreEqual(fatal.Message, entry.Message);
		Assert.AreEqual($"ERROR: Line 3 of Script.txt: {fatal.Message}", entry.FormattedMessage);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void AnalysisResults_KeepTheirOwnDiagnosticsSnapshot()
	{
		using var badWorkspace = TestWorkspace.Create(TestWorkspace.ScriptWithOptionsCommand("MarkerAAA= 1"));
		badWorkspace.WithEnglishLanguageFile();

		using var goodWorkspace = TestWorkspace.CreateClean();

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();

		AnalysisResult badResult = compiler.Analyze(badWorkspace.CreateOptions());
		AnalysisResult goodResult = compiler.Analyze(goodWorkspace.CreateOptions());

		// The first result keeps its own diagnostics after a later run.
		DiagnosticAssert.UnknownOptionsCommand(
			diagnostics: badResult.Diagnostics,
			line: TestWorkspace.OptionsCommandLine,
			command: "MarkerAAA=",
			file: badWorkspace.ScriptFile
		);

		// The later run holds nothing of the earlier one.
		DiagnosticAssert.NoDiagnostics(goodResult.Diagnostics);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void AnalysisResults_Equality_ComparesThePublicMembersOnly()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		using var badWorkspace = TestWorkspace.Create(TestWorkspace.ScriptWithOptionsCommand("MarkerAAA= 1"));

		badWorkspace.WithEnglishLanguageFile();

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();

		AnalysisResult first = compiler.Analyze(workspace.CreateOptions());
		AnalysisResult second = compiler.Analyze(workspace.CreateOptions());
		AnalysisResult bad = compiler.Analyze(badWorkspace.CreateOptions());

		Assert.IsNotNull(first.Outputs, "The analysis keeps its outputs in an internal member.");
		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
		Assert.AreNotEqual(first, bad);

		AnalysisResult withoutInternals = first with { Outputs = null, ScriptModel = null, FirstLanguageData = null, AllLanguageData = [] };

		Assert.AreEqual(first, withoutInternals, "The internal members take no part in the equality.");
		Assert.AreEqual(first.GetHashCode(), withoutInternals.GetHashCode());
		Assert.AreNotEqual(first, first with { Diagnostics = bad.Diagnostics });
		Assert.AreNotEqual(first, first with { Log = first.Log + "x" });
		Assert.AreNotEqual(first, first with { MainLanguageFile = "Other.txt" });
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void CompileResults_Equality_ComparesTheListsByElement()
	{
		static CompileResult Result(string publishedFile)
		{
			return new()
			{
				Success = true,
				Diagnostics = [new CompilerDiagnostic { Severity = DiagnosticSeverity.Warning, Message = "careful" }],
				Log = "log",
				PublishedFiles = [publishedFile],
			};
		}

		Assert.AreEqual(Result("Script.dat"), Result("Script.dat"));
		Assert.AreEqual(Result("Script.dat").GetHashCode(), Result("Script.dat").GetHashCode());
		Assert.AreNotEqual(Result("Script.dat"), Result("English.dat"));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Compile_ReusesCompilerInstance_WithoutStaleDiagnosticsOrLogs()
	{
		const string invalidScript = """
			[Language]
			File= 0,English.txt

			[Options]
			Title= DISABLED
			Security= 0

			[Level]
			Name= Broken Level
			Level= data\broken,0
			""";

		const string validScript = """
			[Language]
			File= 0,English.txt

			[Options]
			Title= DISABLED
			Security= 0

			[Level]
			Name= Playable Level
			Level= data\playable,0
			LoadCamera= 0,0,0,0,0,0,0
			""";

		using var invalidWorkspace = TestWorkspace.Create(invalidScript);
		invalidWorkspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Broken Level"));

		using var validWorkspace = TestWorkspace.Create(TestWorkspace.ExtensionSectionsContent + validScript);
		validWorkspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Playable Level")).WithObjectsH(TestWorkspace.CleanObjectsHContent);

		var compiler = new TRNGCompiler();

		TestCompileResult invalidResult = invalidWorkspace.Compile(compiler: compiler);
		Assert.IsFalse(invalidResult.Success, "The first compile should fail and seed diagnostics/log output.");

		DiagnosticAssert.SingleError(
			diagnostics: invalidResult.Diagnostics,
			line: 8,
			message: "LoadCamera= command is required in level section 'Broken Level'",
			file: invalidWorkspace.ScriptFile
		);

		TestCompileResult validResult = validWorkspace.Compile(compiler: compiler);

		Assert.IsTrue(validResult.Success, validResult.Log);

		// A second compile on the same compiler instance retains neither the previous run's diagnostics nor its log lines.
		DiagnosticAssert.NoDiagnostics(validResult.Diagnostics);
		Assert.AreEqual(string.Join(Environment.NewLine, validResult.Entries.Select(entry => entry.FormattedMessage)), validResult.Log);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Analyze_MissingEngineDirectory_ReportsTheValidationFinding()
	{
		using var workspace = TestWorkspace.CreateCompilable();

		TRNGCompilerOptions options = workspace.CreateOptions() with { EngineDirectory = workspace.PathOf("missing-engine") };
		AnalysisResult analysis = workspace.Analyze(options);

		Assert.IsFalse(analysis.Success);
		DiagnosticAssert.SingleError(analysis.Diagnostics, line: null, $"Engine folder was not found: {options.EngineDirectory}");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Analyze_MissingPluginFolder_WarnsAndSucceeds()
	{
		using var workspace = TestWorkspace.CreateCompilable();

		TRNGCompilerOptions options = workspace.CreateOptions() with { PluginDirectory = workspace.PathOf("missing-plugins") };
		AnalysisResult analysis = workspace.Analyze(options);

		Assert.IsTrue(analysis.Success, analysis.Log);

		DiagnosticAssert.HasWarning(analysis.Diagnostics, line: null, $"Plugin folder was not found: {options.PluginDirectory}");
	}
}
