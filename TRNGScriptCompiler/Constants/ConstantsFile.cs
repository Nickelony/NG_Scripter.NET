using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;

namespace TRNGScriptCompiler.Constants;

/// <summary>The content of a <c>scripter_constants.txt</c> file: its path and raw lines.</summary>
/// <remarks>
/// The run loads it once and shares it between the NG command definitions and the global constants, so both always see
/// the same content.
/// </remarks>
internal sealed record ConstantsFile
{
	/// <summary>Gets the full path of the constants file that was loaded.</summary>
	internal required string FilePath { get; init; }

	/// <summary>Gets the lines of the file as read, in order and split at each CRLF, LF or CR.</summary>
	internal required IReadOnlyList<string> Lines { get; init; }

	/// <summary>
	/// Loads a constants file from an overlay, or from disk
	/// when the overlay lacks it, and warns about its encoding note.
	/// </summary>
	/// <exception cref="IOException">The file is missing or could not be read.</exception>
	/// <exception cref="UnauthorizedAccessException">The file may not be read.</exception>
	internal static ConstantsFile Load(string filePath, DiagnosticSink diagnostics, VirtualFileSystem? overlay = null)
	{
		string[] lines = TextFileUtilities.SplitLines(TextInputReader.ReadAllText(filePath, overlay, diagnostics));
		return new ConstantsFile { FilePath = filePath, Lines = lines };
	}
}
