namespace TRNGScriptCompiler.Hosting;

/// <summary>
/// Contains the result of an async diagnostics request, keyed by a generation number so consumers can discard
/// stale results.
/// </summary>
/// <remarks>
/// Two results are equal when each property is equal, comparing the diagnostics element by element in order.
/// </remarks>
public sealed record DiagnosticsResult
{
	/// <summary>Gets the monotonically increasing generation number for this result.</summary>
	/// <remarks>
	/// Consumers should ignore results with a generation lower than the latest they've seen.
	/// </remarks>
	public required long Generation { get; init; }

	/// <summary>Gets a value indicating whether the analysis found no error.</summary>
	/// <remarks>
	/// A canceled result is not a success.
	/// </remarks>
	public required bool Success { get; init; }

	/// <summary>
	/// Gets a value indicating whether a newer request or the host's disposal canceled the request
	/// before its analysis finished.
	/// </summary>
	/// <remarks>
	/// A canceled result is not a failed analysis: it carries no diagnostics and an empty log, and the consumer
	/// discards it.
	/// </remarks>
	public bool IsCanceled { get; init; }

	/// <summary>Gets the hints, warnings and errors of the analysis, in the order it recorded them.</summary>
	public required IReadOnlyList<CompilerDiagnostic> Diagnostics { get; init; }

	/// <summary>Gets the log text, its lines joined by <see cref="Environment.NewLine"/>.</summary>
	public required string Log { get; init; }

	/// <inheritdoc/>
	public bool Equals(DiagnosticsResult? other)
	{
		return other is not null
			&& Generation == other.Generation
			&& Success == other.Success
			&& IsCanceled == other.IsCanceled
			&& Log == other.Log
			&& Diagnostics.SequenceEqual(other.Diagnostics);
	}

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();

		hash.Add(Generation);
		hash.Add(Success);
		hash.Add(IsCanceled);
		hash.Add(Log);

		foreach (CompilerDiagnostic diagnostic in Diagnostics)
			hash.Add(diagnostic);

		return hash.ToHashCode();
	}
}
