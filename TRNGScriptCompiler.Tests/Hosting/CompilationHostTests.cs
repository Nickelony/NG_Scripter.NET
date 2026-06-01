using TRNGScriptCompiler.Hosting;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Hosting;

[TestClass]
public sealed class CompilationHostTests
{
	// VB6 warns about a script without three extensions in each section, and the port warns about a missing Objects.h and about an
	// Objects.h item that assigns a value, so a clean workspace has the sections and an Objects.h without assignments.
	private const string ObjectsH = TestWorkspace.CleanObjectsHContent;

	private const string CleanScript = TestWorkspace.ExtensionSectionsContent + TestScripts.LanguageAndLevel;

	private static readonly TimeSpan s_gateTimeout = TimeSpan.FromSeconds(30);

	[TestMethod]
	[TestCategory("IO")]
	public async Task FirstRequest_OnACleanWorkspace_IsGenerationOneWithNoDiagnostics()
	{
		using var workspace = TestWorkspace.Create(CleanScript);
		workspace.WithEnglishLanguageFile().WithObjectsH(ObjectsH);

		TRNGCompilerOptions options = workspace.CreateOptions();

		using var host = new CompilationHost();

		DiagnosticsResult result = await host.GetDiagnosticsAsync(options, dirtyFiles: null, cancellationToken: CancellationToken.None);

		Assert.IsTrue(result.Success, $"Diagnostics should succeed: {result.Log}");
		Assert.AreEqual(1, result.Generation, "First request should be generation 1.");

		DiagnosticAssert.NoDiagnostics(result.Diagnostics);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task GetDiagnosticsAsync_OptionsAlone_AnalyzesWithoutDirtyFiles()
	{
		using var workspace = TestWorkspace.Create(CleanScript);
		workspace.WithEnglishLanguageFile().WithObjectsH(ObjectsH);

		TRNGCompilerOptions options = workspace.CreateOptions();

		using var host = new CompilationHost();

		DiagnosticsResult hostResult = await host.GetDiagnosticsAsync(options);
		DiagnosticsResult providerResult = await ((IDiagnosticsProvider)host).GetDiagnosticsAsync(options);

		Assert.IsTrue(hostResult.Success, hostResult.Log);
		Assert.IsTrue(providerResult.Success, providerResult.Log);

		DiagnosticAssert.NoDiagnostics(hostResult.Diagnostics);
		DiagnosticAssert.NoDiagnostics(providerResult.Diagnostics);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task TwoRapidRequests_FirstIsCanceled_SecondSucceeds()
	{
		using var workspace = TestWorkspace.Create(CleanScript);
		workspace.WithEnglishLanguageFile().WithObjectsH(ObjectsH);

		TRNGCompilerOptions options = workspace.CreateOptions();

		using var gate = new AnalysisGate();
		using CompilationHost host = gate.CreateHost();

		// The first request holds at the gate, inside its analysis, until the second request has superseded it.
		Task<DiagnosticsResult> firstTask = host.GetDiagnosticsAsync(options);
		DiagnosticsResult secondResult;

		try
		{
			Assert.IsTrue(gate.Entered.Wait(s_gateTimeout), "The first request never reached the gate.");
			secondResult = await host.GetDiagnosticsAsync(options, dirtyFiles: null, cancellationToken: CancellationToken.None);
		}
		finally
		{
			gate.Released.Set();
		}

		DiagnosticsResult firstResult = await firstTask;

		Assert.IsTrue(firstResult.IsCanceled, "First request must be canceled when superseded by the second.");
		Assert.IsFalse(firstResult.Success, "A canceled request is not a successful one.");
		Assert.AreEqual(1, firstResult.Generation, "First request must keep generation 1.");
		Assert.IsEmpty(firstResult.Diagnostics, "A canceled request must return an empty diagnostics snapshot.");
		Assert.AreEqual(string.Empty, firstResult.Log, "A canceled request must return an empty log.");

		// The second request should have completed normally
		Assert.IsFalse(secondResult.IsCanceled, "The newest request runs to completion.");
		Assert.IsTrue(secondResult.Success, $"Second request should succeed: {secondResult.Log}");
		Assert.AreEqual(2, secondResult.Generation, "Second request should be generation 2.");
		Assert.IsEmpty(secondResult.Diagnostics, "The second request must complete with its own (empty) diagnostics.");

		Assert.AreEqual(
			"******** ANALYSIS COMPLETED ********",
			secondResult.Log.Split(Environment.NewLine)[^1],
			"The analysis ran to its last line."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task DirtyFiles_FlipOutcome_FromMainLanguageFailureToSuccess()
	{
		// The disk workspace lists French.txt as the main language but only provides English.txt.
		// The dirty-file overlay supplies French.txt from memory, which must flip a failing
		// analysis into a successful one.
		const string script = TestScripts.Language + "File= 1,French.txt\n" + TestScripts.Level;

		using var workspace = TestWorkspace.Create(TestWorkspace.ExtensionSectionsContent + script);
		workspace.WithEnglishLanguageFile().WithObjectsH(ObjectsH);

		string scriptDirectory = workspace.ScriptDirectory;
		TRNGCompilerOptions options = workspace.CreateOptions(mainLanguage: "French");

		using var host = new CompilationHost();

		DiagnosticsResult withoutDirtyFiles = await host.GetDiagnosticsAsync(options, dirtyFiles: null, cancellationToken: CancellationToken.None);

		Assert.IsFalse(withoutDirtyFiles.Success, "The main language file is missing from disk, so the analysis must fail.");

		DiagnosticAssert.SingleError(
			diagnostics: withoutDirtyFiles.Diagnostics,
			line: 11,
			message: "Missing language file: French.txt",
			file: Path.Combine(scriptDirectory, "Script.txt")
		);

		var dirtyFiles = new Dictionary<string, string> { [Path.Combine(scriptDirectory, "French.txt")] = TestWorkspace.DefaultEnglishContent };

		DiagnosticsResult withDirtyFiles = await host.GetDiagnosticsAsync(options, dirtyFiles, cancellationToken: CancellationToken.None);

		Assert.IsTrue(withDirtyFiles.Success, $"Dirty French.txt must be found via VFS: {withDirtyFiles.Log}");

		// The dirty-file run resolves the main language from memory.
		DiagnosticAssert.NoDiagnostics(withDirtyFiles.Diagnostics);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task CallerCancellation_Throws_WithTheCallersToken()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		TRNGCompilerOptions options = workspace.CreateOptions();

		using var host = new CompilationHost();

		using var cts = new CancellationTokenSource();
		cts.Cancel();

		OperationCanceledException caught = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
			() => host.GetDiagnosticsAsync(options, dirtyFiles: null, cancellationToken: cts.Token),
			"The caller's own cancellation must throw rather than return a canceled result."
		);

		Assert.AreEqual(cts.Token, caught.CancellationToken, "The exception carries the caller's token.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task Dispose_CancelsTheRequestInFlight_WithACanceledResult()
	{
		using var workspace = TestWorkspace.Create(CleanScript);
		workspace.WithEnglishLanguageFile().WithObjectsH(ObjectsH);

		TRNGCompilerOptions options = workspace.CreateOptions();

		using var gate = new AnalysisGate();
		using CompilationHost host = gate.CreateHost();

		// The request holds at the gate, inside its analysis, until the host is disposed.
		Task<DiagnosticsResult> request = host.GetDiagnosticsAsync(options);

		try
		{
			Assert.IsTrue(gate.Entered.Wait(s_gateTimeout), "The request never reached the gate.");
			host.Dispose();
		}
		finally
		{
			gate.Released.Set();
		}

		DiagnosticsResult result = await request;

		Assert.IsTrue(result.IsCanceled, $"Disposing the host cancels the request in flight: {result.Log}");
		Assert.AreEqual(1, result.Generation);

		DiagnosticAssert.NoDiagnostics(result.Diagnostics);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task Dispose_DuringTheComposition_CancelsTheRequestInFlight()
	{
		using var workspace = TestWorkspace.Create(CleanScript);
		workspace.WithEnglishLanguageFile().WithObjectsH(ObjectsH);

		// The first line the composition of the outputs logs.
		using var gate = new AnalysisGate(entry => entry.Message == "SAVE LANGUAGES.DAT ...");
		using CompilationHost host = gate.CreateHost();

		Task<DiagnosticsResult> request = host.GetDiagnosticsAsync(workspace.CreateOptions());

		try
		{
			Assert.IsTrue(gate.Entered.Wait(s_gateTimeout), "The request never reached the composition.");
			host.Dispose();
		}
		finally
		{
			gate.Released.Set();
		}

		DiagnosticsResult result = await request;

		Assert.IsTrue(result.IsCanceled, $"A request canceled while the outputs compose returns canceled: {result.Log}");
		DiagnosticAssert.NoDiagnostics(result.Diagnostics);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task RejectedDirtyFiles_Throw_WithoutCancelingTheRequestInFlight()
	{
		using var workspace = TestWorkspace.Create(CleanScript);
		workspace.WithEnglishLanguageFile().WithObjectsH(ObjectsH);

		TRNGCompilerOptions options = workspace.CreateOptions();

		using var gate = new AnalysisGate();
		using CompilationHost host = gate.CreateHost();

		Task<DiagnosticsResult> request = host.GetDiagnosticsAsync(options);

		try
		{
			Assert.IsTrue(gate.Entered.Wait(s_gateTimeout), "The request never reached the gate.");

			// Two paths that name one file make the overlay reject the set.
			string path = Path.Combine(workspace.ScriptDirectory, "French.txt");
			var rejected = new Dictionary<string, string> { [path] = "a", [path.ToUpperInvariant()] = "b" };

			await Assert.ThrowsExactlyAsync<ArgumentException>(() => host.GetDiagnosticsAsync(options, rejected));
		}
		finally
		{
			gate.Released.Set();
		}

		DiagnosticsResult result = await request;

		Assert.IsFalse(result.IsCanceled, "A rejected dirty-file set must not cancel the request in flight.");
		Assert.IsTrue(result.Success, result.Log);
		Assert.AreEqual(1, result.Generation);

		DiagnosticsResult next = await host.GetDiagnosticsAsync(options);

		Assert.AreEqual(2, next.Generation, "A rejected request takes no generation.");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void DiagnosticsResults_Equality_ComparesTheDiagnosticsByElement()
	{
		static DiagnosticsResult Result(string message, long generation = 1)
		{
			CompilerDiagnostic error = new()
			{
				Severity = DiagnosticSeverity.Error,
				Message = message,
				LineNumber = 3,
			};

			return new()
			{
				Generation = generation,
				Success = false,
				Diagnostics = [error],
				Log = "log",
			};
		}

		Assert.AreEqual(Result("bad"), Result("bad"));
		Assert.AreEqual(Result("bad").GetHashCode(), Result("bad").GetHashCode());
		Assert.AreNotEqual(Result("bad"), Result("worse"));
		Assert.AreNotEqual(Result("bad"), Result("bad", generation: 2));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public async Task NullOptions_Throws()
	{
		using var host = new CompilationHost();

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			host.GetDiagnosticsAsync(null!, dirtyFiles: null, cancellationToken: CancellationToken.None)
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task Generation_IncrementsMonotonically()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		TRNGCompilerOptions options = workspace.CreateOptions();

		using var host = new CompilationHost();

		DiagnosticsResult r1 = await host.GetDiagnosticsAsync(options, null, CancellationToken.None);
		DiagnosticsResult r2 = await host.GetDiagnosticsAsync(options, null, CancellationToken.None);
		DiagnosticsResult r3 = await host.GetDiagnosticsAsync(options, null, CancellationToken.None);

		Assert.AreEqual(1, r1.Generation);
		Assert.AreEqual(2, r2.Generation);
		Assert.AreEqual(3, r3.Generation);
		Assert.IsTrue(r1.Success);
		Assert.IsTrue(r2.Success);
		Assert.IsTrue(r3.Success);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task Host_ResultsCarryTheirOwnDiagnostics()
	{
		using var goodWorkspace = TestWorkspace.CreateClean();

		using var badWorkspace = TestWorkspace.Create(TestWorkspace.ScriptWithOptionsCommand("MarkerAAA= 1"));
		badWorkspace.WithEnglishLanguageFile();

		using var host = new CompilationHost();

		DiagnosticsResult goodResult = await host.GetDiagnosticsAsync(goodWorkspace.CreateOptions(), null, CancellationToken.None);
		DiagnosticsResult badResult = await host.GetDiagnosticsAsync(badWorkspace.CreateOptions(), null, CancellationToken.None);

		Assert.IsTrue(goodResult.Success, $"Good workspace should analyze: {goodResult.Log}");
		Assert.IsFalse(badResult.Success, "An unknown classic command must be fatal.");

		// The bad result carries its own diagnostics.
		DiagnosticAssert.UnknownOptionsCommand(
			diagnostics: badResult.Diagnostics,
			line: TestWorkspace.OptionsCommandLine,
			command: "MarkerAAA=",
			file: badWorkspace.ScriptFile
		);

		// The good result holds nothing of the bad run.
		DiagnosticAssert.NoDiagnostics(goodResult.Diagnostics);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task Host_OverlappingRequests_DoNotCrossContaminateDiagnostics()
	{
		using var firstWorkspace = TestWorkspace.Create(TestWorkspace.ScriptWithOptionsCommand("MarkerAAA= 1"));
		firstWorkspace.WithEnglishLanguageFile();

		using var secondWorkspace = TestWorkspace.Create(TestWorkspace.ScriptWithOptionsCommand("MarkerBBB= 1"));
		secondWorkspace.WithEnglishLanguageFile();

		// The gate holds the first request, which would report MarkerAAA, inside its analysis until the second request has superseded it.
		using var gate = new AnalysisGate();
		using CompilationHost host = gate.CreateHost();

		Task<DiagnosticsResult> firstTask = host.GetDiagnosticsAsync(firstWorkspace.CreateOptions());
		DiagnosticsResult secondResult;

		try
		{
			Assert.IsTrue(gate.Entered.Wait(s_gateTimeout), "The first request never reached the gate.");

			secondResult = await host.GetDiagnosticsAsync(
				secondWorkspace.CreateOptions(),
				dirtyFiles: null,
				cancellationToken: CancellationToken.None
			);
		}
		finally
		{
			gate.Released.Set();
		}

		DiagnosticsResult firstResult = await firstTask;

		// The second request is never superseded, so it completes with its own diagnostics alone.
		Assert.AreEqual(2, secondResult.Generation);

		DiagnosticAssert.UnknownOptionsCommand(
			diagnostics: secondResult.Diagnostics,
			line: TestWorkspace.OptionsCommandLine,
			command: "MarkerBBB=",
			file: secondWorkspace.ScriptFile
		);

		// The first request is superseded, so it returns an empty result and never carries the second run's diagnostics.
		Assert.AreEqual(1, firstResult.Generation);
		Assert.IsTrue(firstResult.IsCanceled, "The first request is canceled when the second supersedes it.");
		Assert.IsEmpty(firstResult.Diagnostics, "The first result must never carry the second run's diagnostics.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task Host_RequestSupersededAfterItsLastLine_KeepsItsOwnDiagnostics()
	{
		// The workspace holds no Objects.h, so a request without dirty files warns of it, and one that supplies the file does not.
		using var workspace = TestWorkspace.Create(CleanScript);
		workspace.WithEnglishLanguageFile();

		string objectsH = workspace.PathOf("Objects.h");
		var dirtyFiles = new Dictionary<string, string> { [objectsH] = ObjectsH };

		TRNGCompilerOptions options = workspace.CreateOptions();

		// The gate holds the first request at its last log line, when its diagnostics are final, until the second has completed.
		// Nothing checks the cancellation after that line, so the superseded request still returns its own result.
		using var gate = new AnalysisGate(entry => entry.Message == "******** ANALYSIS COMPLETED ********");
		using CompilationHost host = gate.CreateHost();

		Task<DiagnosticsResult> firstTask = host.GetDiagnosticsAsync(options);
		DiagnosticsResult secondResult;

		try
		{
			Assert.IsTrue(gate.Entered.Wait(s_gateTimeout), "The first request never reached its last log line.");
			secondResult = await host.GetDiagnosticsAsync(options, dirtyFiles, CancellationToken.None);
		}
		finally
		{
			gate.Released.Set();
		}

		DiagnosticsResult firstResult = await firstTask;

		Assert.AreEqual(2, secondResult.Generation);
		Assert.IsTrue(secondResult.Success, secondResult.Log);
		Assert.IsFalse(secondResult.IsCanceled);

		DiagnosticAssert.NoDiagnostics(secondResult.Diagnostics);

		Assert.AreEqual(1, firstResult.Generation);
		Assert.IsTrue(firstResult.Success, firstResult.Log);
		Assert.IsFalse(firstResult.IsCanceled, "A request past its last cancellation check completes, whatever supersedes it.");

		DiagnosticAssert.HasWarning(
			diagnostics: firstResult.Diagnostics,
			line: null,
			message: $"Objects.h was not found: {objectsH}",
			file: objectsH
		);

		Assert.HasCount(1, firstResult.Diagnostics, "The first result holds its own warning alone.");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public async Task Host_AfterDispose_ThrowsObjectDisposed()
	{
		var host = new CompilationHost();
		host.Dispose();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
			host.GetDiagnosticsAsync(TRNGCompilerOptions.CreateDefaults(TestWorkspace.TempRoot), null, CancellationToken.None)
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public async Task Host_AfterDispose_ThrowsObjectDisposedForRejectedDirtyFilesToo()
	{
		var host = new CompilationHost();
		host.Dispose();

		// Two paths that name one file make the overlay reject the set.
		string path = Path.Combine(TestWorkspace.TempRoot, "French.txt");
		var rejected = new Dictionary<string, string> { [path] = "a", [path.ToUpperInvariant()] = "b" };

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
			host.GetDiagnosticsAsync(TRNGCompilerOptions.CreateDefaults(TestWorkspace.TempRoot), rejected, CancellationToken.None)
		);
	}

	/// <summary>
	/// A compiler whose first log line, or the first that a predicate accepts, waits for the test, so a request stops
	/// inside its analysis at a known point.
	/// </summary>
	/// <remarks>
	/// Without a predicate the gate holds the first line of the analysis, which it logs
	/// before it reads a file. Later lines, of any request, pass the gate.
	/// </remarks>
	private sealed class AnalysisGate : IDisposable
	{
		private readonly TRNGCompiler _compiler = new() { ConciseLogging = true };
		private readonly Func<LogEntry, bool> _holdsAt;

		private int _held;

		internal AnalysisGate(Func<LogEntry, bool>? holdsAt = null)
		{
			_holdsAt = holdsAt ?? (static entry => true);
			_compiler.LogEntryProduced += HoldTheFirstLine;
		}

		/// <summary>Gets the event the gate raises when an analysis reaches it.</summary>
		internal ManualResetEventSlim Entered { get; } = new();

		/// <summary>Gets the event the test sets to let the analysis go on.</summary>
		internal ManualResetEventSlim Released { get; } = new();

		internal CompilationHost CreateHost() => new(_compiler);

		public void Dispose()
		{
			_compiler.LogEntryProduced -= HoldTheFirstLine;

			Entered.Dispose();
			Released.Dispose();
		}

		private void HoldTheFirstLine(object? sender, LogEntryProducedEventArgs e)
		{
			if (!_holdsAt(e.Entry) || Interlocked.Exchange(ref _held, 1) != 0)
				return;

			Entered.Set();
			Released.Wait(s_gateTimeout);
		}
	}
}
