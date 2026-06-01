namespace TRNGScriptCompiler.Hosting;

/// <summary>Provides async diagnostics for a TRNG script project using generation-based cancellation.</summary>
public interface IDiagnosticsProvider
{
	/// <summary>
	/// Requests a project's diagnostics, cancels the one in flight, and reads dirty files
	/// as a <see cref="VirtualFileSystem"/>.
	/// </summary>
	/// <param name="options">The options of the project: its folders and its constants file.</param>
	/// <param name="dirtyFiles">
	/// The unsaved text of the open files, keyed by full path as the <see cref="VirtualFileSystem"/>
	/// constructor takes them, or <see langword="null"/> for none.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The diagnostics of the analysis, numbered by the generation of the request.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// A key of <paramref name="dirtyFiles"/> is not a valid path, or two keys name the same file.
	/// </exception>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> is canceled; a request superseded before its analysis composed the outputs
	/// returns a canceled result.
	/// </exception>
	/// <remarks>
	/// The method is asynchronous, so the exceptions above arrive through the returned task and not at the call.
	/// </remarks>
	Task<DiagnosticsResult> GetDiagnosticsAsync(
		TRNGCompilerOptions options,
		IReadOnlyDictionary<string, string>? dirtyFiles = null,
		CancellationToken cancellationToken = default
	);
}
