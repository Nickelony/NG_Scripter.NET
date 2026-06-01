using System.IO.Enumeration;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;

namespace TRNGScriptCompiler.Plugins;

/// <summary>
/// Reads the <c>Plugin*.script</c> files of a plugin folder and its immediate subfolders, a top-level file
/// winning (PD-87).
/// </summary>
/// <remarks>
/// NG Center reads the top level, yet its installer makes a <c>Plugin_X\</c> folder (Dialog_InstallPlugin.frm:222-262),
/// and TombIDE one per plugin.
/// </remarks>
internal static class PluginDiscovery
{
	private const string ScriptPattern = "Plugin*.script";

	/// <summary>
	/// Reads the plugin scripts of a folder and its immediate subfolders, one per plugin name, ordered by name.
	/// </summary>
	/// <remarks>
	/// Overlay scripts join and replace those on disk; a skipped copy gets a verbose note, an unreadable script,
	/// plugin folder or subfolder a warning.
	/// </remarks>
	internal static IReadOnlyList<PluginScript> Discover(string folder, VirtualFileSystem? overlay, CompilerLog logger, DiagnosticSink diagnostics)
	{
		if (!VirtualFileSystem.DirectoryExists(folder, overlay))
		{
			logger.LogVerbose($"The plugin folder {folder} does not exist, so no plugin script is read");
			return [];
		}

		var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		if (!TryListScripts(directory: folder, overlay, diagnostics, scripts: out string[] topLevelScripts))
			return [];

		foreach (string path in topLevelScripts)
			chosen.TryAdd(Path.GetFileNameWithoutExtension(path), path);

		foreach (string subfolder in ListSubfolders(folder, overlay, diagnostics))
		{
			if (!TryListScripts(directory: subfolder, overlay, diagnostics, out string[] scripts))
				continue;

			foreach (string path in scripts)
			{
				string name = Path.GetFileNameWithoutExtension(path);

				if (chosen.TryAdd(name, path))
					continue;

				// Only the verbose note needs the comparison, so a concise log skips the reads.
				if (logger.ConciseLogging)
					continue;

				bool? sameContent = HaveSameContent(chosen[name], path, overlay);

				if (sameContent is null)
				{
					logger.LogVerbose(
						$"The plugin script {path} cannot be compared with {chosen[name]}, which is read instead, because one of the two cannot be read"
					);
				}
				else if (!sameContent.Value)
				{
					logger.LogVerbose($"The plugin script {path} differs from {chosen[name]}, which is read instead");
				}
			}
		}

		var discovered = new List<PluginScript>();

		foreach ((string name, string path) in chosen.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
		{
			if (TryRead(path, overlay, diagnostics, out string[]? lines))
			{
				discovered.Add(new PluginScript(name, path, lines));
				logger.LogVerbose($"Discovered plugin: {name}");
			}
		}

		if (discovered.Count > 0)
			logger.Log($"Found {discovered.Count} plugin file(s)");

		return discovered;
	}

	/// <summary>Lists the plugin scripts of one folder, or warns that it cannot be read.</summary>
	private static bool TryListScripts(string directory, VirtualFileSystem? overlay, DiagnosticSink diagnostics, out string[] scripts)
	{
		try
		{
			IEnumerable<string> inOverlay = overlay?.EnumerateFiles(directory).Where(IsPluginScript) ?? [];
			// The names are sorted before the duplicates go, so of two names a case-sensitive file system holds apart, the same one wins on every
			// run.
			IEnumerable<string> onDisk = PathUtilities.EnumerateFilesInsensitive(directory, ScriptPattern).Order(StringComparer.Ordinal);

			scripts =
			[
				.. inOverlay
					.Concat(onDisk)
					.DistinctBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
					.Order(StringComparer.OrdinalIgnoreCase),
			];

			return true;
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			diagnostics.Warning($"The plugin folder {directory} cannot be read, so no plugin script is read from it: {ex.Reason()}", exception: ex);
			scripts = [];
			return false;
		}
	}

	private static bool IsPluginScript(string path) =>
		FileSystemName.MatchesSimpleExpression(ScriptPattern, Path.GetFileName(path), ignoreCase: true);

	private static string[] ListSubfolders(string folder, VirtualFileSystem? overlay, DiagnosticSink diagnostics)
	{
		try
		{
			IEnumerable<string> inOverlay = overlay?.EnumerateDirectories(folder) ?? [];
			IEnumerable<string> onDisk = Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal) : [];

			return
			[
				.. inOverlay
					.Concat(onDisk)
					.DistinctBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
					.Order(StringComparer.OrdinalIgnoreCase),
			];
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			diagnostics.Warning($"The subfolders of {folder} cannot be listed, so their plugin scripts are skipped: {ex.Reason()}", exception: ex);
			return [];
		}
	}

	/// <summary>Compares two plugin scripts, or returns <see langword="null"/> when either cannot be read.</summary>
	private static bool? HaveSameContent(string first, string second, VirtualFileSystem? overlay)
	{
		try
		{
			if (overlay is not null && (overlay.HasFile(first) || overlay.HasFile(second)))
				return VirtualFileSystem.ReadAllText(first, overlay, out _) == VirtualFileSystem.ReadAllText(second, overlay, out _);

			return InputFileReader.ReadAllBytes(first).AsSpan().SequenceEqual(InputFileReader.ReadAllBytes(second));
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			return null;
		}
	}

	private static bool TryRead(
		string path,
		VirtualFileSystem? overlay,
		DiagnosticSink diagnostics,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string[]? lines
	)
	{
		try
		{
			lines = TextFileUtilities.SplitLines(TextInputReader.ReadAllText(path, overlay, diagnostics));
			return true;
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			diagnostics.Warning(
				$"The plugin script {Path.GetFileName(path)} cannot be read, so its constants are unknown: {ex.Reason()}",
				SourceLocation.InFile(path),
				ex
			);

			lines = null;
			return false;
		}
	}
}
