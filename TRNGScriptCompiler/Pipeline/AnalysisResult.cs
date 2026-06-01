using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Pipeline;

namespace TRNGScriptCompiler;

/// <summary>
/// Contains the result of an analysis: parsing, compiling and composing the outputs without writing a file.
/// </summary>
/// <remarks>
/// Two results are equal when their public members are equal, so the internal members take no part.
/// </remarks>
public sealed record AnalysisResult
{
	/// <summary>Gets a value indicating whether the analysis found no error.</summary>
	public required bool Success { get; init; }

	/// <summary>
	/// Gets the hints, warnings and errors of the analysis, as a snapshot that never changes
	/// after the analysis returns.
	/// </summary>
	public required IReadOnlyList<CompilerDiagnostic> Diagnostics { get; init; }

	/// <summary>Gets the log text, its lines joined by <see cref="Environment.NewLine"/>.</summary>
	public required string Log { get; init; }

	/// <summary>
	/// Gets the file name of the selected main language, or <see langword="null"/> when the analysis failed
	/// before every language parsed.
	/// </summary>
	public string? MainLanguageFile { get; init; }

	/// <summary>
	/// Gets the script model after the NG compile, or <see langword="null"/> if the script or its first language fails
	/// to parse.
	/// </summary>
	/// <remarks>
	/// A later language that fails leaves it set.
	/// </remarks>
	internal ScriptModel? ScriptModel { get; init; }

	/// <summary>
	/// Gets the first language data parsed before <c>Script.dat</c> encoding, or <see langword="null"/> when the first
	/// language or a later one failed to parse.
	/// </summary>
	/// <remarks>
	/// This is a test seam: only the tests read it, to show what the analysis parsed.
	/// </remarks>
	internal LanguageData? FirstLanguageData { get; init; }

	/// <summary>Gets all parsed language data, in the order listed in <c>Script.txt</c>.</summary>
	/// <remarks>
	/// The first entry is the same instance as <see cref="FirstLanguageData"/>. This is a test seam: only the tests
	/// read it.
	/// </remarks>
	internal IReadOnlyList<LanguageData> AllLanguageData { get; init; } = [];

	/// <summary>
	/// Gets the outputs the analysis composed, which a compile publishes,
	/// or <see langword="null"/> when the analysis failed.
	/// </summary>
	internal CompiledOutputs? Outputs { get; init; }

	/// <inheritdoc/>
	public bool Equals(AnalysisResult? other)
	{
		return other is not null
			&& Success == other.Success
			&& MainLanguageFile == other.MainLanguageFile
			&& Log == other.Log
			&& Diagnostics.SequenceEqual(other.Diagnostics);
	}

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();

		hash.Add(Success);
		hash.Add(MainLanguageFile);
		hash.Add(Log);

		foreach (CompilerDiagnostic diagnostic in Diagnostics)
			hash.Add(diagnostic);

		return hash.ToHashCode();
	}
}
