namespace TRNGScriptCompiler;

/// <summary>Contains the result of a compilation: its diagnostics, its log and the files it published.</summary>
/// <remarks>
/// Two results are equal when their lists hold equal elements in the same order.
/// </remarks>
public sealed record CompileResult
{
	/// <summary>Gets a value indicating whether the compilation published every output without a fatal error.</summary>
	public required bool Success { get; init; }

	/// <summary>
	/// Gets the hints, warnings and errors of the compilation, as a snapshot that never changes
	/// after the compilation returns.
	/// </summary>
	public required IReadOnlyList<CompilerDiagnostic> Diagnostics { get; init; }

	/// <summary>
	/// Gets the log text of the analysis and the publication, its lines joined by <see cref="Environment.NewLine"/>.
	/// </summary>
	public required string Log { get; init; }

	/// <summary>
	/// Gets the full paths of the files the compilation wrote, in the order it moved them into place.
	/// </summary>
	/// <remarks>
	/// The list is empty when the compilation failed, because a failed publication restores every output it had moved.
	/// </remarks>
	public required IReadOnlyList<string> PublishedFiles { get; init; }

	/// <inheritdoc/>
	public bool Equals(CompileResult? other)
	{
		return other is not null
			&& Success == other.Success
			&& Log == other.Log
			&& Diagnostics.SequenceEqual(other.Diagnostics)
			&& PublishedFiles.SequenceEqual(other.PublishedFiles);
	}

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();

		hash.Add(Success);
		hash.Add(Log);

		foreach (CompilerDiagnostic diagnostic in Diagnostics)
			hash.Add(diagnostic);

		foreach (string file in PublishedFiles)
			hash.Add(file);

		return hash.ToHashCode();
	}
}
