using System.IO.Enumeration;

namespace TRNGScriptCompiler.Kernel.Infrastructure;

/// <summary>
/// Provides the folder-entry and file-name helpers that stay correct on case-sensitive file systems (Linux, macOS).
/// </summary>
internal static class PathUtilities
{
	/// <summary>
	/// Chooses the folder entry a path segment names without regard to case, or <see langword="null"/> when none does.
	/// </summary>
	/// <remarks>
	/// An exact spelling wins, so a case-sensitive system opens the one named, then the ordinal-first variant,
	/// whatever the listing order.
	/// </remarks>
	internal static string? ChooseEntry(IEnumerable<string> names, string segment)
	{
		string? chosen = null;

		foreach (string name in names)
		{
			if (!name.Equals(segment, StringComparison.OrdinalIgnoreCase))
				continue;

			if (name.Equals(segment, StringComparison.Ordinal))
				return name;

			if (chosen is null || string.CompareOrdinal(name, chosen) < 0)
				chosen = name;
		}

		return chosen;
	}

	/// <summary>Tells whether two paths end in the same file name, without regard to case.</summary>
	internal static bool HaveSameFileName(string left, string right) => Path.GetFileName(left).IgnoreCaseEquals(Path.GetFileName(right));

	/// <summary>
	/// Enumerates the files of one folder whose names match a DOS-style wildcard pattern, without regard to case.
	/// </summary>
	/// <remarks>
	/// The pattern takes <c>*</c> and <c>?</c>, such as <c>"Plugin*.script"</c>, and a missing folder yields no files.
	/// It enters no subfolder, so no link can loop it, and it lists a symbolic link to a file as the file.
	/// </remarks>
	internal static IEnumerable<string> EnumerateFilesInsensitive(string path, string searchPattern)
	{
		if (!Directory.Exists(path))
			return [];

		var options = new EnumerationOptions
		{
			MatchType = MatchType.Simple,
			AttributesToSkip = 0,
			IgnoreInaccessible = false,
		};

		// DOS wildcard matching with ordinal case-folding, without building a regex per call.
		return Directory
			.EnumerateFiles(path, "*", options)
			.Where(file => FileSystemName.MatchesSimpleExpression(searchPattern, Path.GetFileName(file), ignoreCase: true));
	}
}
