using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;

namespace TRNGScriptCompiler.Kernel.Runtime;

/// <summary>
/// Holds the state of one compilation run: its log, diagnostics, defines, files read and folder listings.
/// </summary>
/// <remarks>
/// A run starts empty, so two compilations never share defines, diagnostics or log lines.
/// </remarks>
internal sealed class CompilationRun
{
	private readonly HashSet<string> _filesRead = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Initializes a run whose log hands its lines to no callback, a test seam.</summary>
	internal CompilationRun(bool conciseLogging = false)
		: this(conciseLogging, logEntryProduced: null) { }

	/// <summary>Initializes a new compilation run whose log hands each line to a callback.</summary>
	/// <remarks>
	/// <paramref name="logEntryProduced"/> receives the lines in log order, on the logging thread.
	/// </remarks>
	internal CompilationRun(bool conciseLogging, Action<LogEntry>? logEntryProduced)
	{
		Logger = new CompilerLog(conciseLogging, logEntryProduced);
		Diagnostics = new DiagnosticSink(Logger);
	}

	internal CompilerLog Logger { get; }

	internal DiagnosticSink Diagnostics { get; }

	/// <summary>
	/// Gets the clock, in seconds since midnight, that an unseeded compile reads for its security padding;
	/// <see langword="null"/> reads the real clock.
	/// </summary>
	/// <remarks>
	/// This is a test seam: a test fixes the clock to show that the padding follows the timer (PD-07).
	/// </remarks>
	internal Func<double>? TimerSeconds { get; init; }

	/// <summary>Gets the <c>#DEFINE</c> values the run's source files declare.</summary>
	internal DefineRegistry Defines { get; } = new();

	/// <summary>Gets the resolver of the analysis, which lists each folder at most once.</summary>
	/// <remarks>
	/// The analysis writes no file, so a listing cannot go stale while it runs. Publication writes and moves files,
	/// so it resolves through <see cref="PathResolver.Uncached"/>.
	/// </remarks>
	internal PathResolver PathResolver { get; } = new();

	/// <summary>Records a file the run reads, so the stale-file cleanup after publication never deletes it.</summary>
	internal void RecordFileRead(string path) => _filesRead.Add(Path.GetFullPath(path));

	/// <summary>Tells whether the run read the file at a path.</summary>
	/// <returns>
	/// <see langword="true"/> when <see cref="RecordFileRead"/> recorded that path; otherwise, <see langword="false"/>.
	/// </returns>
	internal bool WasFileRead(string path) => _filesRead.Contains(Path.GetFullPath(path));
}
