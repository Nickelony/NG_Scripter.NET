namespace TRNGScriptCompiler.Cli;

/// <summary>
/// Holds what the console process does around a run: the Ctrl+C handling, the wait for a key and the last-resort
/// handler of an unexpected fault.
/// </summary>
/// <remarks>
/// The process reaches the console through two callbacks and gives the interrupt to <see cref="HandleInterrupt"/>,
/// so a test drives both without a real console or a signal.
/// </remarks>
internal sealed class CliProcess : IDisposable
{
	private const int FatalExitCode = 1;

	private readonly CancellationTokenSource _cancellation = new();
	private readonly Action<string, ConsoleColor?> _writeError;
	private readonly Action _waitForExit;

	private volatile bool _compileStarted;
	private volatile bool _runEnded;

	/// <param name="writeError">
	/// The callback that writes text to standard error, as <see cref="CliApplication"/> takes it.
	/// </param>
	/// <param name="waitForExit">The callback that waits for a key press before the process exits.</param>
	internal CliProcess(Action<string, ConsoleColor?> writeError, Action waitForExit)
	{
		_writeError = writeError;
		_waitForExit = waitForExit;
	}

	/// <summary>
	/// Gets the token that the first Ctrl+C during a compile cancels, which the compile checks
	/// until its publication starts.
	/// </summary>
	internal CancellationToken CancellationToken => _cancellation.Token;

	/// <summary>
	/// Records that a compile starts, so a Ctrl+C from now on cancels it instead of ending the process.
	/// </summary>
	internal void MarkCompileStarted() => _compileStarted = true;

	/// <summary>
	/// Handles one Ctrl+C: the first during a compile cancels it before it publishes, and a started publication runs
	/// to its end, because a killed process runs no rollback.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when the process goes on, which is the first press during a compile;
	/// <see langword="false"/> for any other press, which lets the process end where it stands.
	/// </returns>
	/// <remarks>
	/// A press before any compile starts, such as at a prompt for a path, has nothing to cancel,
	/// and neither has one once the application returned, such as at the wait for a key, so each counts
	/// as a second press, as does one after the process disposed this instance.
	/// </remarks>
	internal bool HandleInterrupt()
	{
		if (!_compileStarted || _runEnded || _cancellation.IsCancellationRequested)
			return false;

		try
		{
			_cancellation.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// The handler runs on its own thread, so a Ctrl+C can arrive after the run ended and the process disposed
			// the source; nothing is left to cancel, and the process is ending anyway.
			return false;
		}

		_writeError("Canceling; press Ctrl+C again to end the process at once." + Environment.NewLine, null);
		return true;
	}

	/// <summary>
	/// Runs the application and waits for a key when it asks to; an unexpected fault ends the run with its message
	/// and the exit code 1 rather than a crash dialog.
	/// </summary>
	/// <returns>The exit code of the run.</returns>
	/// <remarks>
	/// A fault waits for a key only in interactive mode, when the run had no arguments, because a script that passed
	/// arguments has no one to press it.
	/// </remarks>
	internal int Run(Func<string[], CliRunResult> application, string[] args)
	{
		try
		{
			CliRunResult result = application(args);

			_runEnded = true;

			if (result.WaitForKey)
				_waitForExit();

			return result.ExitCode;
		}
		catch (Exception ex)
		{
			_runEnded = true;

			_writeError($"\nFatal error: {ex.Message}\n", ConsoleColor.Red);
			_writeError($"Stack trace:\n{ex.StackTrace ?? "No stack trace available."}\n", ConsoleColor.Red);

			if (args.Length == 0)
				_waitForExit();

			return FatalExitCode;
		}
	}

	/// <inheritdoc/>
	public void Dispose() => _cancellation.Dispose();
}
