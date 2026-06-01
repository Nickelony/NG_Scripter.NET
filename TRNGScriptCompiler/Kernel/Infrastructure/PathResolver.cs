using System.IO.Enumeration;

namespace TRNGScriptCompiler.Kernel.Infrastructure;

/// <summary>
/// Resolves paths to their on-disk casing, and keeps the folder listings it reads unless it is the uncached one.
/// </summary>
/// <remarks>
/// A kept listing is never refreshed, so a file created after its folder was listed stays unseen by that resolver;
/// use one only for a phase that writes no file. A resolver that keeps listings is not safe to share between threads.
/// </remarks>
internal sealed class PathResolver
{
	private static readonly EnumerationOptions s_entryOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = true };

	private readonly Dictionary<string, string[]?>? _listings;

	/// <summary>Initializes a resolver that lists each folder at most once.</summary>
	internal PathResolver()
		: this(cacheListings: true) { }

	private PathResolver(bool cacheListings) => _listings = cacheListings ? new Dictionary<string, string[]?>(StringComparer.Ordinal) : null;

	/// <summary>
	/// Gets the resolver that keeps no listing, so it sees every change on disk and is safe to share.
	/// </summary>
	/// <remarks>
	/// Publication, which writes files, and the option helpers resolve through it.
	/// </remarks>
	internal static PathResolver Uncached { get; } = new(cacheListings: false);

	/// <summary>
	/// Gets the number of folder listings this resolver has read, which stays 0 for <see cref="Uncached"/>.
	/// </summary>
	/// <remarks>
	/// A test seam: the tests read it to show that a folder is listed once.
	/// </remarks>
	internal int ListingCount { get; private set; }

	/// <summary>
	/// Resolves a full path with each segment in its on-disk casing, so mixed-case paths work on case-sensitive
	/// file systems.
	/// </summary>
	/// <remarks>
	/// A segment that does not exist, such as an output file about to be created, keeps its input casing,
	/// and resolution continues below it.
	/// </remarks>
	internal string ResolveFullPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return path;

		string fullPath;

		try
		{
			fullPath = Path.GetFullPath(path);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
		{
			return path;
		}

		string root = Path.GetPathRoot(fullPath) ?? string.Empty;

		if (root.Length == 0)
			return fullPath;

		string current = root;

		string[] segments = fullPath[root.Length..]
			.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

		foreach (string segment in segments)
			current = ResolveEntry(current, segment);

		return current;
	}

	/// <summary>
	/// Resolves a Windows-style script path in a folder, the current one when empty,
	/// as <see cref="ResolveFullPath"/> does.
	/// </summary>
	/// <remarks>
	/// The backslashes of any script path separate folders even on Unix, unlike those in the folder's names;
	/// a path rooted as written ignores it.
	/// </remarks>
	internal string ResolveScriptPath(string folder, string scriptPath)
	{
		string hostPath = scriptPath.Replace('\\', Path.DirectorySeparatorChar);
		return ResolveFullPath(Path.IsPathRooted(scriptPath) ? hostPath : Path.Join(folder, hostPath));
	}

	/// <summary>
	/// Resolves a file or folder name inside a folder whose path is already resolved, listing that folder alone.
	/// </summary>
	/// <remarks>
	/// A name no entry matches keeps its casing, as <see cref="ResolveFullPath"/> keeps a missing segment's.
	/// </remarks>
	internal string ResolveEntry(string parent, string segment)
	{
		string[]? names = NamesIn(parent, segment);

		if (names is not null && PathUtilities.ChooseEntry(names, segment) is { } name)
			return Path.Combine(parent, name);

		return Path.Combine(parent, segment);
	}

	/// <summary>
	/// Gets the names in a folder that can match a segment, or <see langword="null"/> when the folder cannot be listed.
	/// </summary>
	/// <remarks>
	/// A resolver that keeps listings reads every name of the folder once, and remembers a folder it could not list.
	/// </remarks>
	private string[]? NamesIn(string parent, string segment)
	{
		if (_listings is null)
			return ListNames(parent, segment);

		if (!_listings.TryGetValue(parent, out string[]? names))
		{
			names = ListNames(parent, segment: null);
			_listings.Add(parent, names);
			ListingCount++;
		}

		return names;
	}

	private static string[]? ListNames(string parent, string? segment)
	{
		try
		{
			// The name is compared literally, so a segment holding '*' or '?', legal on Unix, matches only itself.
			var entries = new FileSystemEnumerable<string>(parent, (ref FileSystemEntry entry) => entry.FileName.ToString(), s_entryOptions)
			{
				ShouldIncludePredicate = (ref FileSystemEntry entry) =>
					segment is null || entry.FileName.Equals(segment, StringComparison.OrdinalIgnoreCase),
			};

			return [.. entries];
		}
		catch (Exception ex) when (ex.IsFileAccessFault() || ex is ArgumentException)
		{
			// Parent is missing or inaccessible - the caller keeps the requested casing.
			return null;
		}
	}
}
