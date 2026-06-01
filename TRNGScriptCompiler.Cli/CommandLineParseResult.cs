namespace TRNGScriptCompiler.Cli;

/// <summary>The parsed command line: a compile request, a help request or an error.</summary>
internal abstract record CommandLineParseResult
{
	/// <summary>
	/// Keeps the nested outcomes the only subtypes, so a caller that handles each handles every command line.
	/// </summary>
	private protected CommandLineParseResult() { }

	/// <summary>A help request.</summary>
	internal sealed record Help : CommandLineParseResult;

	/// <summary>A version request.</summary>
	internal sealed record Version : CommandLineParseResult;

	/// <summary>A command line that asks for a compile.</summary>
	/// <param name="Options">The compile options.</param>
	/// <param name="ConciseLogging">Whether verbose log lines are suppressed.</param>
	/// <param name="WaitForKey">Whether the process waits for a key press before it exits.</param>
	/// <param name="WriteLogFile">
	/// Whether the log is also written to <see cref="CliLogFile.FileName"/> in the engine folder.
	/// </param>
	internal sealed record Compile(TRNGCompilerOptions Options, bool ConciseLogging, bool WaitForKey, bool WriteLogFile) : CommandLineParseResult;

	/// <summary>A rejected command line.</summary>
	/// <param name="Message">The reason, which names the fix.</param>
	internal sealed record Error(string Message) : CommandLineParseResult;
}
