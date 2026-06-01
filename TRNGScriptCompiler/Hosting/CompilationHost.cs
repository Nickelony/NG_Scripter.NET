namespace TRNGScriptCompiler.Hosting;

/// <summary>
/// In-process diagnostics host that runs one analysis per request and cancels the request a newer one supersedes.
/// </summary>
/// <remarks>
/// The generation and the cancellation source change under one lock. A new request starts at once, so analyses overlap
/// until the older one observes its cancellation, and each result holds only its own analysis. A request whose dirty
/// files are rejected fails through its returned task before it supersedes anything. A request superseded
/// after its analysis composed the outputs returns its own result, not a canceled one.
/// </remarks>
public sealed class CompilationHost : IDiagnosticsProvider, IDisposable
{
	private readonly TRNGCompiler _compiler;
	private readonly object _lock = new();

	private long _generation;
	private volatile bool _disposed;
	private CancellationTokenSource? _currentCts;

	/// <summary>Initializes a host whose analyses log concisely.</summary>
	public CompilationHost()
		: this(new TRNGCompiler { ConciseLogging = true }) { }

	/// <summary>Initializes a host over a compiler.</summary>
	/// <remarks>
	/// This is a test seam: a test holds an analysis at a log line of the compiler.
	/// </remarks>
	internal CompilationHost(TRNGCompiler compiler) => _compiler = compiler;

	/// <inheritdoc/>
	/// <exception cref="ObjectDisposedException">The host is disposed.</exception>
	/// <remarks>
	/// Any thread may call it, also while another call runs; a newer call cancels the one in flight.
	/// </remarks>
	public async Task<DiagnosticsResult> GetDiagnosticsAsync(
		TRNGCompilerOptions options,
		IReadOnlyDictionary<string, string>? dirtyFiles = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(options);

		// A disposed host throws the documented exception even for a dirty-file set the overlay would reject.
		ObjectDisposedException.ThrowIf(_disposed, this);

		// Build the overlay before the lock, so a dirty-file set it rejects throws without canceling the request in flight.
		VirtualFileSystem? overlay = dirtyFiles is { Count: > 0 } ? new VirtualFileSystem(dirtyFiles) : null;

		// Atomically capture the generation for this request and cancel any prior in-flight request
		long generation;
		CancellationTokenSource linkedCts;
		CancellationTokenSource? previousCts;

		lock (_lock)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			generation = ++_generation;

			// Take the previous in-flight request's source out under the lock, and cancel it once the lock is released
			previousCts = _currentCts;

			// Create a new linked CTS that ties the external token to our internal cancellation
			_currentCts = new CancellationTokenSource();
			linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _currentCts.Token);
		}

		// Canceling runs the callbacks of the older request on this thread, so it happens outside the lock
		CancelAndDispose(previousCts);

		try
		{
			// Offload the synchronous analysis to a thread-pool thread
			AnalysisResult result = await Task.Run(() => _compiler.Analyze(options, overlay, linkedCts.Token), linkedCts.Token).ConfigureAwait(false);

			// The result carries its own diagnostics snapshot - never read compiler state here,
			// because a newer run may already have replaced the published run.
			return new DiagnosticsResult
			{
				Generation = generation,
				Success = result.Success,
				Diagnostics = result.Diagnostics,
				Log = result.Log,
			};
		}
		catch (OperationCanceledException)
		{
			// The caller's own cancellation propagates with the caller's token; only a supersede or Dispose returns a canceled result.
			cancellationToken.ThrowIfCancellationRequested();

			return new DiagnosticsResult
			{
				Generation = generation,
				Success = false,
				IsCanceled = true,
				Diagnostics = [],
				Log = string.Empty,
			};
		}
		finally
		{
			linkedCts.Dispose();
		}
	}

	/// <summary>Cancels the request in flight and releases the host's cancellation state.</summary>
	/// <remarks>
	/// The request in flight returns a canceled result unless its outputs are already composed.
	/// Later calls to <see cref="GetDiagnosticsAsync"/> throw <see cref="ObjectDisposedException"/>.
	/// </remarks>
	public void Dispose()
	{
		CancellationTokenSource? currentCts;

		lock (_lock)
		{
			if (_disposed)
				return;

			_disposed = true;

			currentCts = _currentCts;
			_currentCts = null;
		}

		CancelAndDispose(currentCts);
	}

	private static void CancelAndDispose(CancellationTokenSource? source)
	{
		source?.Cancel();
		source?.Dispose();
	}
}
