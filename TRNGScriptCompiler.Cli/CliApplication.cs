using System.Diagnostics;
using System.Reflection;

namespace TRNGScriptCompiler.Cli;

/// <summary>
/// Runs the console compiler: parses the command line or prompts for the paths, compiles, and prints the log
/// and the outcome.
/// </summary>
/// <remarks>
/// The application writes through two callbacks, one for standard output and one for standard error,
/// and reads through a reader, so a test drives it without a console. A line goes to standard error when it reports
/// an error or a compile that did not succeed, and every other line goes to standard output. The log file,
/// when one is asked for, takes a copy of each log line and of the outcome, and none of it goes to either stream.
/// The banner opens a compile, so help and a rejected command line print none.
/// </remarks>
internal sealed class CliApplication
{
	private const int SuccessExitCode = 0;
	private const int FailureExitCode = 1;

	/// <summary>
	/// The shell's code for a process that an interrupt ended (128 plus SIGINT), so a batch file's
	/// <c>if errorlevel 1</c> stops on it too.
	/// </summary>
	private const int CanceledExitCode = 130;

	private const string SuccessMessage = "Compilation completed successfully";
	private const string FailureMessage = "Compilation failed";
	private const string CanceledMessage = "Compilation canceled";

	private readonly InteractivePrompts _prompts;

	private readonly Action<string, ConsoleColor?> _write;
	private readonly Action<string, ConsoleColor?> _writeError;

	private readonly CancellationToken _cancellationToken;
	private readonly Action? _compileStarted;

	/// <param name="input">The reader that interactive mode reads the paths from.</param>
	/// <param name="write">
	/// The callback that writes text to standard output, in a color or in the default one, without adding a line break.
	/// </param>
	/// <param name="writeError">
	/// The callback that writes text to standard error, as <paramref name="write"/> does.
	/// </param>
	/// <param name="compileStarted">
	/// The callback that runs as a compile starts, which tells the process that a Ctrl+C from then on cancels
	/// the compile instead of ending the process; none when <see langword="null"/>.
	/// </param>
	/// <param name="cancellationToken">
	/// The token that cancels a compile before its publication starts, which Ctrl+C cancels in the process.
	/// </param>
	internal CliApplication(
		TextReader input,
		Action<string, ConsoleColor?> write,
		Action<string, ConsoleColor?> writeError,
		Action? compileStarted = null,
		CancellationToken cancellationToken = default
	)
	{
		_prompts = new InteractivePrompts(input, write);
		_write = write;
		_writeError = writeError;
		_cancellationToken = cancellationToken;
		_compileStarted = compileStarted;
	}

	/// <summary>Gets the banner that opens a compile, with the empty line that follows it.</summary>
	internal static string BannerText { get; } =
		string.Join(
			Environment.NewLine,
			"=======================================",
			"   TRNG Script Compiler (C# Edition)   ",
			"        Rewritten by: Nickelony        ",
			"=======================================",
			// Two empty pieces end the last line and add the empty line.
			string.Empty,
			string.Empty
		);

	/// <summary>Gets the help text shown for <c>--help</c>.</summary>
	internal static string HelpText { get; } =
		$"""
			Usage:
			  NG_Scripter <engine-folder> [options]
			  NG_Scripter <path>{Path.DirectorySeparatorChar}script{Path.DirectorySeparatorChar}script.txt [options]

			Positional arguments:
			  <engine-folder>             Path to the TRNG engine folder.
			  <path>{Path.DirectorySeparatorChar}script{Path.DirectorySeparatorChar}script.txt    NG Center's form: the engine folder is the folder above script.
			                              This form waits for a key at the end unless -NoWait is given.

			Options:
			  --script-folder <path>      Path to the script folder.
			  -sf

			  --plugin-folder <path>      Path to the plugin folder.
			  -pf                         Defaults to <engine-folder>{Path.DirectorySeparatorChar}NG_Center, or to <engine-folder> without it.

			  --main-language <language>  Set the language whose .dat ships with the game.
			  -ml                         Name it by file name or base name, or by its zero-based place in [Language].
			                              Defaults to the first listed language if omitted.

			  --constants <path>          Path to scripter_constants.txt.
			  -c                          Defaults to the compiler directory if omitted.

			  --wait                      Wait for a key press before exiting.
			  -w

			  --quiet / -q                Enable concise (quiet) logging.
			  --verbose / -v              Enable verbose logging (default).

			  --version                   Show the version and exit.

			  --help / -h / -? / /?       Show this help message and exit.

			NG Center's switches:
			  -Concise                    The same as --quiet.
			  -Log                        Also write the log to <engine-folder>{Path.DirectorySeparatorChar}LastCompilerLog.txt.
			  -NoWait                     Exit without waiting for a key.
			  -NoMsgBox                   Accepted; the console shows no message box.

			Interactive mode:
			  Run with no arguments to be prompted for the required paths, quoted or not.

			""";

	/// <summary>Gets the version text shown for <c>--version</c>: the executable name, then the version.</summary>
	internal static string VersionText { get; } = ReadVersionText();

	private static string ReadVersionText()
	{
		Assembly assembly = typeof(CliApplication).Assembly;

		string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

		// Directory.Build.props leaves the source revision out of the version, and this cut keeps a build that puts it back (build metadata
		// after a plus sign) from showing it.
		string version = informational is null
			? assembly.GetName().Version?.ToString(fieldCount: 3) ?? "unknown"
			: informational.Split('+')[0];

		return $"NG_Scripter {version}";
	}

	/// <summary>Runs the application for the supplied arguments, or in interactive mode when there are none.</summary>
	internal CliRunResult Run(string[] args)
	{
		if (args.Length == 0)
			return RunInteractive();

		switch (CommandLineParser.Parse(args))
		{
			case CommandLineParseResult.Help:
				_write(HelpText, null);
				return new CliRunResult(SuccessExitCode, WaitForKey: false);

			case CommandLineParseResult.Version:
				WriteLine(VersionText, null);
				return new CliRunResult(SuccessExitCode, WaitForKey: false);

			case CommandLineParseResult.Compile request:
				_write(BannerText, null);
				int exitCode = Compile(request.Options, request.ConciseLogging, request.WriteLogFile);

				// A canceled compile has no result to read, so it does not wait.
				return new CliRunResult(exitCode, request.WaitForKey && exitCode != CanceledExitCode);

			case CommandLineParseResult.Error error:
				WriteErrorLine($"Error: {error.Message}");
				return new CliRunResult(FailureExitCode, WaitForKey: false);

			default:
				throw new UnreachableException();
		}
	}

	private CliRunResult RunInteractive()
	{
		// The session compiles unless the engine folder answer is empty, so the banner opens it before the prompts.
		_write(BannerText, null);

		if (_prompts.Ask() is not { } options)
		{
			WriteErrorLine("Error: No engine folder specified");
			return new CliRunResult(FailureExitCode, WaitForKey: false);
		}

		int exitCode = Compile(options, conciseLogging: false, writeLogFile: false);

		// An interactive session waits, so the user can read the result, unless Ctrl+C canceled it.
		return new CliRunResult(exitCode, WaitForKey: exitCode != CanceledExitCode);
	}

	/// <summary>Compiles a script folder, prints each log line as it arrives, and prints the outcome once.</summary>
	/// <returns>
	/// The exit code: 0 when the compilation and the log file, when one is asked for, succeed; 130 when the token
	/// canceled the compile before its publication started; otherwise 1.
	/// </returns>
	private int Compile(TRNGCompilerOptions options, bool conciseLogging, bool writeLogFile)
	{
		_compileStarted?.Invoke();

		List<string>? logLines = writeLogFile ? [] : null;

		var compiler = new TRNGCompiler { ConciseLogging = conciseLogging };

		compiler.LogEntryProduced += (_, e) =>
		{
			WriteLogEntry(e.Entry);
			logLines?.Add(e.Entry.FormattedMessage);
		};

		WriteLine(string.Empty, null);

		int exitCode;

		try
		{
			exitCode = compiler.Compile(options, _cancellationToken).Success ? SuccessExitCode : FailureExitCode;
		}
		catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
		{
			// The compile stops before it publishes, so the engine folder holds the outputs of the last compile.
			exitCode = CanceledExitCode;
		}

		string outcome = exitCode switch
		{
			SuccessExitCode => SuccessMessage,
			CanceledExitCode => CanceledMessage,
			_ => FailureMessage,
		};

		WriteLine(string.Empty, null);

		if (exitCode == SuccessExitCode)
			WriteLine(outcome, ConsoleColor.Green);
		else
			WriteErrorLine(outcome);

		if (logLines is null)
			return exitCode;

		logLines.Add(outcome);

		if (CliLogFile.TryWrite(options.EngineDirectory, logLines, out string? logError))
			return exitCode;

		WriteErrorLine(logError);

		return exitCode != SuccessExitCode ? exitCode : FailureExitCode;
	}

	/// <summary>Writes a log entry, to standard error when it is an error and to standard output otherwise.</summary>
	private void WriteLogEntry(LogEntry entry)
	{
		switch (entry.Severity)
		{
			case LogSeverity.Error:
				WriteErrorLine(entry.FormattedMessage);
				break;

			case LogSeverity.Warning:
				WriteLine(entry.FormattedMessage, ConsoleColor.Yellow);
				break;

			default:
				WriteLine(entry.FormattedMessage, null);
				break;
		}
	}

	private void WriteLine(string text, ConsoleColor? color) => _write(text + Environment.NewLine, color);

	private void WriteErrorLine(string text) => _writeError(text + Environment.NewLine, ConsoleColor.Red);
}
