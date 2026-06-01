using System.Runtime.CompilerServices;

namespace TRNGScriptCompiler.Kernel.Diagnostics;

/// <summary>
/// Captures the log text of a compilation run and hands each line to the run's <see cref="LogEntry"/> callback.
/// </summary>
/// <remarks>
/// The log carries progress and verbose text; errors and warnings go through <see cref="DiagnosticSink"/>,
/// which writes their lines here.
/// </remarks>
internal sealed class CompilerLog
{
	private readonly List<string> _messages = [];
	private readonly Action<LogEntry>? _entryProduced;

	private List<LogEntry>? _heldEntries;

	/// <summary>Initializes a log that hands its lines to no callback, a test seam.</summary>
	internal CompilerLog(bool conciseLogging)
		: this(conciseLogging, entryProduced: null) { }

	/// <summary>Initializes a new compiler log that hands each line to a callback.</summary>
	/// <remarks>
	/// <paramref name="entryProduced"/> receives the lines in log order, on the logging thread.
	/// </remarks>
	internal CompilerLog(bool conciseLogging, Action<LogEntry>? entryProduced)
	{
		ConciseLogging = conciseLogging;
		_entryProduced = entryProduced;
	}

	internal bool ConciseLogging { get; }

	private static string ApplyIndent(string message, int indent) => indent > 0 ? $"{new string('\t', indent)}{message}" : message;

	private void Emit(LogSeverity severity, string message, string formattedMessage, string? sourceFile, int? lineNumber)
	{
		if (_entryProduced is null)
			return;

		var entry = new LogEntry
		{
			Timestamp = DateTimeOffset.UtcNow,
			Severity = severity,
			Message = message,
			FormattedMessage = formattedMessage,
			SourceFile = sourceFile,
			LineNumber = lineNumber,
		};

		if (_heldEntries is not null)
			_heldEntries.Add(entry);
		else
			_entryProduced(entry);
	}

	/// <summary>
	/// Holds back the callback of every line logged until <see cref="ReleaseHeldEntries"/>, while the log text keeps
	/// each line.
	/// </summary>
	/// <remarks>
	/// Hold the entries around a file-system critical section, so a callback that throws cannot interrupt it.
	/// </remarks>
	internal void HoldEntries() => _heldEntries ??= [];

	/// <summary>Hands every held line to the callback, in the order logged, and stops holding.</summary>
	/// <remarks>
	/// A callback that throws stops the release, and the lines after it reach only the log text.
	/// </remarks>
	internal void ReleaseHeldEntries()
	{
		if (_heldEntries is not { } held)
			return;

		_heldEntries = null;

		foreach (LogEntry entry in held)
			_entryProduced?.Invoke(entry);
	}

	/// <summary>Logs a plain informational message, indented by <paramref name="indent"/> tabs.</summary>
	internal void Log(string message, int indent = 0) => WriteMessage(severity: LogSeverity.Info, message, indent);

	/// <summary>
	/// Logs a verbose message, indented by <paramref name="indent"/> tabs, only when concise logging is disabled.
	/// </summary>
	internal void LogVerbose(string message, int indent = 0)
	{
		if (ConciseLogging)
			return;

		WriteMessage(severity: LogSeverity.Verbose, message, indent);
	}

	/// <summary>
	/// Logs a verbose message from an interpolated string, which builds no text when concise logging is enabled.
	/// </summary>
	internal void LogVerbose([InterpolatedStringHandlerArgument("")] ref VerboseLogHandler message, int indent = 0)
	{
		if (ConciseLogging)
			return;

		WriteMessage(severity: LogSeverity.Verbose, message: message.ToStringAndClear(), indent);
	}

	private void WriteMessage(LogSeverity severity, string message, int indent)
	{
		string indented = ApplyIndent(message, indent);
		_messages.Add(indented);

		Emit(severity, message, formattedMessage: indented, sourceFile: null, lineNumber: null);
	}

	/// <summary>
	/// Writes a diagnostic's log line with its <c>ERROR:</c> or <c>WARNING:</c> prefix,
	/// for <see cref="DiagnosticSink"/>.
	/// </summary>
	/// <remarks>
	/// <paramref name="text"/> carries the location that <paramref name="message"/> lacks; the 1-based line
	/// is <see langword="null"/> for a general diagnostic.
	/// </remarks>
	internal void LogDiagnostic(bool isError, string text, string message, string? sourceFile, int? lineNumber)
	{
		string line = isError ? $"ERROR: {text}" : $"WARNING: {text}";
		_messages.Add(line);

		Emit(severity: isError ? LogSeverity.Error : LogSeverity.Warning, message, formattedMessage: line, sourceFile, lineNumber);
	}

	/// <summary>Logs a blank separator line, as a verbose entry that concise logging suppresses.</summary>
	internal void LogSeparator()
	{
		if (ConciseLogging)
			return;

		WriteMessage(severity: LogSeverity.Verbose, message: string.Empty, indent: 0);
	}

	/// <summary>Returns the full captured log as a single string.</summary>
	internal string GetText() => string.Join(Environment.NewLine, _messages);
}
