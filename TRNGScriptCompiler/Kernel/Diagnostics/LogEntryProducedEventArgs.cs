namespace TRNGScriptCompiler;

/// <summary>Carries the log line that a compilation logged.</summary>
public sealed class LogEntryProducedEventArgs : EventArgs
{
	internal LogEntryProducedEventArgs(LogEntry entry) => Entry = entry;

	/// <summary>Gets the line the compiler logged.</summary>
	public LogEntry Entry { get; }
}
