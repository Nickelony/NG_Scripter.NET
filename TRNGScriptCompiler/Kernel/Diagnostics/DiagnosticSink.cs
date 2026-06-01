namespace TRNGScriptCompiler.Kernel.Diagnostics;

/// <summary>
/// Collects the errors, warnings and hints of a single compilation run and writes each error and warning
/// to the run's log.
/// </summary>
/// <remarks>
/// The sink is the only way the compiler reports an error, a warning or a hint; a run with an error fails.
/// </remarks>
internal sealed class DiagnosticSink
{
	private readonly CompilerLog _logger;

	internal DiagnosticSink(CompilerLog logger) => _logger = logger;

	private readonly List<CompilerDiagnostic> _diagnostics = [];

	internal IReadOnlyList<CompilerDiagnostic> Recorded => _diagnostics;

	/// <summary>Records an error, which fails the run.</summary>
	/// <param name="message">A full sentence without trailing punctuation or location text.</param>
	/// <param name="location">The source the error points at, or <see langword="null"/> for a general error.</param>
	/// <param name="exception">The exception that caused the error, kept as evidence.</param>
	internal void Error(string message, SourceLocation? location = null, Exception? exception = null) =>
		Record(severity: DiagnosticSeverity.Error, message, location, exception);

	/// <summary>Records a warning, which leaves the run's result unchanged.</summary>
	/// <param name="message">A full sentence without trailing punctuation or location text.</param>
	/// <param name="location">
	/// The source the warning points at, or <see langword="null"/> for a general warning.
	/// </param>
	/// <param name="exception">The exception that caused the warning, kept as evidence.</param>
	internal void Warning(string message, SourceLocation? location = null, Exception? exception = null) =>
		Record(severity: DiagnosticSeverity.Warning, message, location, exception);

	/// <summary>Records a hint, a suggestion about input the compiler accepts.</summary>
	/// <param name="message">A full sentence without trailing punctuation or location text.</param>
	/// <param name="location">The source the hint points at, or <see langword="null"/> for a general hint.</param>
	/// <remarks>
	/// A hint writes no log line: it reaches the result's diagnostics and no more.
	/// </remarks>
	internal void Hint(string message, SourceLocation? location = null) =>
		Record(severity: DiagnosticSeverity.Hint, message, location, exception: null);

	private void Record(DiagnosticSeverity severity, string message, SourceLocation? location, Exception? exception)
	{
		SourceLocation place = location ?? default;

		bool hasLine = place.Line is > 0;

		var diagnostic = new CompilerDiagnostic
		{
			LineNumber = hasLine ? place.Line : null,
			Severity = severity,
			Message = message,
			SourceFile = place.File,
			Column = hasLine ? place.Column : 0,
			Length = hasLine ? place.Length : 0,
			Exception = exception,
		};

		_diagnostics.Add(diagnostic);

		if (diagnostic.IsHint)
			return;

		_logger.LogDiagnostic(
			isError: diagnostic.IsError,
			text: $"{FormatLocation(place)}: {message}",
			message,
			sourceFile: place.File,
			lineNumber: diagnostic.LineNumber
		);
	}

	/// <summary>Formats a diagnostic location as <c>Line N of name.txt</c>, <c>Line N</c> or <c>General</c>.</summary>
	/// <remarks>
	/// A 1-based line in a file reads <c>Line N of name.txt</c>, with the file name alone, and a line that names
	/// no file reads <c>Line N</c>. A diagnostic that is not tied to a line reads <c>General</c>.
	/// </remarks>
	internal static string FormatLocation(SourceLocation location)
	{
		if (location.Line is not > 0)
			return "General";

		return location.File is null ? $"Line {location.Line}" : $"Line {location.Line} of {Path.GetFileName(location.File)}";
	}

	internal bool HasErrors() => _diagnostics.Any(error => error.IsError);

	/// <summary>Logs a separator and the count of errors and warnings the run recorded.</summary>
	/// <remarks>
	/// Each error and warning reached the log when it was recorded, so the summary
	/// does not repeat it, and it does not count a hint, which has no log line.
	/// </remarks>
	internal void LogFailureSummary()
	{
		int errorCount = _diagnostics.Count(diagnostic => diagnostic.IsError);
		int warningCount = _diagnostics.Count(diagnostic => diagnostic.IsWarning);

		_logger.LogSeparator();
		_logger.Log($"{errorCount} error(s) and {warningCount} warning(s) found");
	}
}
