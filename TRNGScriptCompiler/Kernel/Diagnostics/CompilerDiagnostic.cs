namespace TRNGScriptCompiler;

/// <summary>
/// Holds one compiler hint, warning or error: its message, its source file and line, and the span to highlight.
/// </summary>
/// <remarks>
/// The <see cref="Severity"/> says which: an error fails the compilation, a warning leaves its result unchanged,
/// and a hint makes a suggestion about input the compiler accepts.
/// </remarks>
public sealed record CompilerDiagnostic
{
	/// <summary>Gets the 1-based source line, or <see langword="null"/> for a diagnostic tied to no line.</summary>
	/// <remarks>
	/// The log line of a diagnostic tied to no line reads <c>General</c> where a line number stands.
	/// </remarks>
	public int? LineNumber { get; init; }

	/// <summary>
	/// Gets the 0-based character (UTF-16 code unit) offset within the source line where the diagnostic span begins.
	/// </summary>
	/// <remarks>
	/// The source line is the line as its file holds it, so a leading tab or a run of spaces counts, and a line
	/// that a <c>&gt;</c> continues counts in its first physical line. When 0 and <see cref="Length"/> is also 0,
	/// the consumer should highlight the entire line, which is also how a term that the source line does not hold
	/// as written is marked. The source line ends where its comment starts.
	/// </remarks>
	public int Column { get; init; }

	/// <summary>Gets the length in characters (UTF-16 code units) of the diagnostic span.</summary>
	/// <remarks>
	/// When 0, the consumer should highlight from <see cref="Column"/> to end of line.
	/// </remarks>
	public int Length { get; init; }

	/// <summary>Gets how much the diagnostic matters.</summary>
	/// <remarks>
	/// A hint has no line in the log and takes no part in the count of warnings.
	/// </remarks>
	public required DiagnosticSeverity Severity { get; init; }

	/// <summary>Gets a value indicating whether the diagnostic is an error, which fails the compilation.</summary>
	public bool IsError => Severity == DiagnosticSeverity.Error;

	/// <summary>
	/// Gets a value indicating whether the diagnostic is a warning, which leaves the compilation's result unchanged.
	/// </summary>
	public bool IsWarning => Severity == DiagnosticSeverity.Warning;

	/// <summary>Gets a value indicating whether the diagnostic is a hint, the lowest severity.</summary>
	public bool IsHint => Severity == DiagnosticSeverity.Hint;

	/// <summary>
	/// Gets the message, a full sentence without the location and the <c>ERROR:</c> or <c>WARNING:</c> prefix
	/// of its log line.
	/// </summary>
	public required string Message { get; init; }

	/// <summary>
	/// Gets the full path of the source file the diagnostic points at, or <see langword="null"/> for a diagnostic tied
	/// to no file.
	/// </summary>
	public string? SourceFile { get; init; }

	/// <summary>
	/// Gets the exception that caused the diagnostic, or <see langword="null"/> when no exception did.
	/// </summary>
	/// <remarks>
	/// A diagnostic of a failed file operation, such as a list, a stage or a move, keeps the exception as evidence,
	/// and the message names its reason.
	/// </remarks>
	public Exception? Exception { get; init; }
}
