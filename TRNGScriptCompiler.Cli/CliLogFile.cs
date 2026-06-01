using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace TRNGScriptCompiler.Cli;

/// <summary>Writes the <c>-Log</c> file of a compile in the engine folder.</summary>
internal static class CliLogFile
{
	/// <summary>
	/// The file in the engine folder that <c>-Log</c> writes, the name NG Center gives it (Module1.bas:15886).
	/// </summary>
	internal const string FileName = "LastCompilerLog.txt";

	private static readonly Encoding s_windows1252 =
		CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? throw new InvalidOperationException("The Windows-1252 code page is not available.");

	/// <summary>Replaces the log file of an engine folder with the run's lines in Windows-1252.</summary>
	/// <param name="engineDirectory">The engine folder that holds the file.</param>
	/// <param name="lines">The lines of the log and the outcome.</param>
	/// <param name="error">The error line to print when the write failed; otherwise, <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the file was written; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// NG Center deletes the file and appends each line (Module1.bas:15883-15909).
	/// </remarks>
	internal static bool TryWrite(string engineDirectory, IReadOnlyList<string> lines, [NotNullWhen(false)] out string? error)
	{
		string path = Path.Combine(engineDirectory, FileName);

		try
		{
			File.WriteAllLines(path, lines, s_windows1252);
			error = null;
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			error = $"Error: The log file {path} could not be written: {ex.Message}";
			return false;
		}
	}
}
