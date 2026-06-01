using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Pipeline;

namespace TRNGScriptCompiler;

/// <summary>
/// Compiles a TRNG script folder into <c>Script.dat</c> and the main language's <c>.dat</c>,
/// or analyzes it without writing a file.
/// </summary>
/// <remarks>
/// Each call has its own run, so overlapping calls share no state, but two <see cref="Compile"/>
/// calls on one engine folder race on its files. The handlers of <see cref="LogEntryProduced"/> receive the lines
/// of overlapping calls interleaved, each on its call's thread, so a handler must be thread-safe.
/// </remarks>
public sealed class TRNGCompiler
{
	/// <summary>
	/// Raised after each line an analysis or a compilation logs, in log order, on the thread that runs the call.
	/// </summary>
	/// <remarks>
	/// A host that updates a UI marshals the handler to its UI thread. A handler's exception escapes the call,
	/// so <see cref="Compile"/> returns no result. The publication's entries arrive after its last file operation,
	/// so a throw among them cannot leave the engine folder half published, but the outputs are then in place.
	/// </remarks>
	public event EventHandler<LogEntryProducedEventArgs>? LogEntryProduced;

	/// <summary>
	/// Gets a value indicating whether the log drops verbose entries and keeps errors, warnings and progress lines.
	/// </summary>
	public bool ConciseLogging { get; init; }

	/// <summary>
	/// Gets the clock, in seconds since midnight, that an unseeded compile reads for its security padding;
	/// <see langword="null"/> reads the real clock.
	/// </summary>
	/// <remarks>
	/// This is a test seam: a test fixes the clock to show that the padding follows the timer (PD-07).
	/// </remarks>
	internal Func<double>? TimerSeconds { get; init; }

	/// <summary>
	/// Analyzes a script folder in memory and reports every error <see cref="Compile"/> reports, except a failure
	/// to write a file.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
	/// <remarks>
	/// Each text of <paramref name="overlay"/>, such as an editor's unsaved buffer, replaces the text file at its path.
	/// A binary input, such as an import file or an image, and a file the overlay lacks are read from disk.
	/// <see cref="Compile"/> takes no overlay.
	/// </remarks>
	public AnalysisResult Analyze(TRNGCompilerOptions options, VirtualFileSystem? overlay = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);
		return RunAnalysis(run: CreateRun(), options, overlay, cancellationToken);
	}

	/// <summary>
	/// Compiles a script folder and publishes its outputs to the engine folder, replacing every output or none.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> was canceled before publication started.
	/// </exception>
	public CompileResult Compile(TRNGCompilerOptions options, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);

		// One run spans the analysis and the publication, so the publication sees the defines and the files the analysis read.
		CompilationRun run = CreateRun();
		AnalysisResult analysis = RunAnalysis(run, options, overlay: null, cancellationToken);

		if (!analysis.Success)
		{
			run.Diagnostics.LogFailureSummary();
			return CreateCompileResult(run, success: false, publishedFiles: []);
		}

		cancellationToken.ThrowIfCancellationRequested();

		// VB6 logs this banner before it reads the script (Module1.bas:8818), and the port logs it where publication starts,
		// so the log of a failed analysis announces no step that never runs (PD-93).
		run.Logger.Log("**** START COMPILING ****");
		run.Logger.Log($"TRNG Script Compiler - Engine folder: {options.EngineDirectory}");

		var publisher = new OutputPublisher(run);

		bool published = publisher.TryPublish(
			analysis,
			PathResolver.Uncached.ResolveFullPath(options.EngineDirectory),
			out IReadOnlyList<string> publishedFiles
		);

		if (!published)
			run.Diagnostics.LogFailureSummary();

		return CreateCompileResult(run, published, publishedFiles);
	}

	private CompilationRun CreateRun() => new(ConciseLogging, OnLogEntryProduced) { TimerSeconds = TimerSeconds };

	private void OnLogEntryProduced(LogEntry entry) => LogEntryProduced?.Invoke(this, new LogEntryProducedEventArgs(entry));

	private static AnalysisResult RunAnalysis(
		CompilationRun run,
		TRNGCompilerOptions options,
		VirtualFileSystem? overlay,
		CancellationToken cancellationToken
	)
	{
		run.Logger.Log("**** START ANALYZING ****");
		run.Logger.Log($"TRNG Script Compiler - Engine folder: {options.EngineDirectory}");

		return ScriptAnalyzer.Analyze(run, options, overlay, token: cancellationToken);
	}

	private static CompileResult CreateCompileResult(CompilationRun run, bool success, IReadOnlyList<string> publishedFiles)
	{
		return new()
		{
			Success = success,
			Diagnostics = [.. run.Diagnostics.Recorded],
			Log = run.Logger.GetText(),
			PublishedFiles = publishedFiles,
		};
	}
}
