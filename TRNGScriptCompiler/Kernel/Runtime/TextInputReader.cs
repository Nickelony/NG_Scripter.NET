using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Kernel.Runtime;

/// <summary>Reads a text input of a run and warns at its file about the encoding note of the read.</summary>
/// <remarks>
/// A failed read throws the I/O or access exception of <see cref="VirtualFileSystem.ReadAllText"/>, so each caller
/// keeps its own reaction to it.
/// </remarks>
internal static class TextInputReader
{
	/// <summary>Reads a text file from an overlay, or from disk, and warns about its encoding note.</summary>
	/// <param name="path">The full path of the file.</param>
	/// <param name="overlay">The in-memory files a host supplies, or <see langword="null"/> for the disk alone.</param>
	/// <param name="diagnostics">The sink that receives the warning.</param>
	/// <param name="notedFiles">
	/// The files that already took their warning, which gains this one; the note shows for each file
	/// once when the caller reads it more than once, and always when <see langword="null"/>.
	/// </param>
	/// <returns>The text of the file.</returns>
	internal static string ReadAllText(string path, VirtualFileSystem? overlay, DiagnosticSink diagnostics, HashSet<string>? notedFiles = null)
	{
		string text = VirtualFileSystem.ReadAllText(path, overlay, out string? encodingNote);

		if (encodingNote is not null && (notedFiles is null || notedFiles.Add(path)))
			diagnostics.Warning(encodingNote, SourceLocation.InFile(path));

		return text;
	}
}
