namespace TRNGScriptCompiler;

/// <summary>
/// Holds one log line that the <c>LogEntryProduced</c> event streams, stamped in UTC, with its severity and message.
/// </summary>
public sealed record LogEntry
{
	/// <summary>Gets the UTC time at which the compiler logged the line.</summary>
	public required DateTimeOffset Timestamp { get; init; }

	/// <summary>Gets how much the line matters, from a verbose detail to an error.</summary>
	public required LogSeverity Severity { get; init; }

	/// <summary>Gets the text of the line, without its indent, severity prefix or location.</summary>
	public required string Message { get; init; }

	/// <summary>
	/// Gets the line as the log writes it: <see cref="Message"/> with its indent, and a diagnostic's severity prefix
	/// and location.
	/// </summary>
	public required string FormattedMessage { get; init; }

	/// <summary>
	/// Gets the full path of the file the line is about, or <see langword="null"/> when no file applies.
	/// </summary>
	public string? SourceFile { get; init; }

	/// <summary>Gets the 1-based line the entry is about, or <see langword="null"/> when no line applies.</summary>
	public int? LineNumber { get; init; }
}
