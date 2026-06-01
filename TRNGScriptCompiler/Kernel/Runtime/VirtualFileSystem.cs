using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using TRNGScriptCompiler.Kernel.Infrastructure;

namespace TRNGScriptCompiler;

/// <summary>
/// An immutable set of in-memory text files that an analysis reads instead of the files on disk at the same paths.
/// </summary>
/// <remarks>
/// Paths match by full form without regard to case, a file the set lacks is read from disk, and a folder exists
/// when the set holds a file below it.
/// </remarks>
public sealed class VirtualFileSystem
{
	private readonly FrozenDictionary<string, string> _files;
	private readonly FrozenSet<string> _directories;

	/// <summary>Initializes a new virtual file system that holds a copy of the supplied files.</summary>
	/// <param name="files">The file text keyed by path; a relative path resolves against the current directory.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="files"/> or one of its texts is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">A path is invalid, or two paths name the same file.</exception>
	public VirtualFileSystem(IReadOnlyDictionary<string, string> files)
	{
		ArgumentNullException.ThrowIfNull(files);

		var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach ((string path, string content) in files)
		{
			ArgumentNullException.ThrowIfNull(content, nameof(files));

			string fullPath;

			try
			{
				fullPath = Normalize(path);
			}
			catch (Exception ex) when (ex is NotSupportedException or PathTooLongException)
			{
				throw new ArgumentException($"The path '{path}' is not a valid file path: {ex.Message}", nameof(files), ex);
			}

			if (!normalized.TryAdd(fullPath, content))
				throw new ArgumentException($"Two paths name the file {fullPath}; keep one of them", nameof(files));

			string? directory = Path.GetDirectoryName(fullPath);

			while (directory is not null && directories.Add(directory))
				directory = Path.GetDirectoryName(directory);
		}

		_files = normalized.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
		_directories = directories.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
	}

	internal bool TryGetFile(string path, [NotNullWhen(true)] out string? content)
	{
		if (NormalizeOrNull(path) is { } normalized)
			return _files.TryGetValue(normalized, out content);

		content = null;
		return false;
	}

	internal bool HasFile(string path) => NormalizeOrNull(path) is { } normalized && _files.ContainsKey(normalized);

	/// <summary>Returns whether the set holds a file below a folder.</summary>
	internal bool HasDirectory(string path) => NormalizeOrNull(path) is { } normalized && _directories.Contains(normalized);

	/// <summary>Lists the files the set holds directly in a folder.</summary>
	/// <returns>The full paths of the files.</returns>
	internal IEnumerable<string> EnumerateFiles(string directory)
	{
		if (NormalizeOrNull(directory) is not { } normalized)
			return [];

		return _files.Keys.Where(path => normalized.IgnoreCaseEquals(Path.GetDirectoryName(path)));
	}

	/// <summary>Lists the folders that hold a file of the set and sit directly in a folder.</summary>
	/// <returns>The full paths of the folders.</returns>
	internal IEnumerable<string> EnumerateDirectories(string directory)
	{
		if (NormalizeOrNull(directory) is not { } normalized)
			return [];

		return _directories.Where(path => normalized.IgnoreCaseEquals(Path.GetDirectoryName(path)));
	}

	/// <summary>Returns whether a file exists in an overlay, when one is given, or on disk.</summary>
	internal static bool FileExists(string path, VirtualFileSystem? overlay) => (overlay is not null && overlay.HasFile(path)) || File.Exists(path);

	/// <summary>Returns whether a folder exists in an overlay, when one is given, or on disk.</summary>
	internal static bool DirectoryExists(string path, VirtualFileSystem? overlay) =>
		(overlay is not null && overlay.HasDirectory(path)) || Directory.Exists(path);

	/// <summary>
	/// Reads a text file from an overlay, or from disk by <see cref="TextFileUtilities.ReadAllText"/> when the overlay
	/// lacks it.
	/// </summary>
	/// <remarks>
	/// Every text input of a run is read here, through <c>TextInputReader</c>, which warns about the note,
	/// except a read that only compares two files, which calls this directly. Only a disk read sets
	/// <paramref name="encodingNote"/> or throws an I/O or access exception.
	/// </remarks>
	internal static string ReadAllText(string path, VirtualFileSystem? overlay, out string? encodingNote)
	{
		if (overlay is not null && overlay.TryGetFile(path, out string? content))
		{
			encodingNote = null;
			return content;
		}

		return TextFileUtilities.ReadAllText(path, out encodingNote);
	}

	private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

	/// <summary>
	/// Normalizes a lookup path, or returns <see langword="null"/> when it does not normalize,
	/// such as with a U+0000 character.
	/// </summary>
	/// <remarks>
	/// Such a path names no file of the set, so the caller's disk check reports it.
	/// </remarks>
	private static string? NormalizeOrNull(string path)
	{
		try
		{
			return Normalize(path);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return null;
		}
	}
}
