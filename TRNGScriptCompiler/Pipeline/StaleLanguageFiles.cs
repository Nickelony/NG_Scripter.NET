using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Script;

namespace TRNGScriptCompiler.Pipeline;

/// <summary>Names the language <c>.dat</c> files that a publication removes from the engine folder as stale.</summary>
/// <param name="diagnostics">
/// The sink that receives the warning for an old <c>Script.dat</c> that cannot be read.
/// </param>
/// <remarks>
/// A file is stale when the old <c>Script.dat</c> or the new script names it and the publication
/// does not write it (PD-23). The caller never deletes a file the run read.
/// </remarks>
internal sealed class StaleLanguageFiles(DiagnosticSink diagnostics)
{
	/// <summary>Collects the names of the stale language files.</summary>
	/// <param name="scriptDatPath">The full path of the <c>Script.dat</c> that the publication replaces.</param>
	/// <param name="languageFiles">The language files the new script lists.</param>
	/// <param name="publishedFileNames">The names of the files the publication writes, which are never stale.</param>
	/// <returns>The file names, matched without regard to case.</returns>
	/// <remarks>
	/// The old <c>Script.dat</c> is read before the new one replaces it, and a missing or unreadable one lists none.
	/// </remarks>
	internal HashSet<string> Collect(string scriptDatPath, IEnumerable<string> languageFiles, IEnumerable<string> publishedFileNames)
	{
		HashSet<string> staleFileNames = ReadLanguageNames(scriptDatPath);

		staleFileNames.UnionWith(languageFiles.Select(languageFile => Path.ChangeExtension(languageFile, ".DAT")));
		staleFileNames.ExceptWith(publishedFileNames);

		return staleFileNames;
	}

	private HashSet<string> ReadLanguageNames(string scriptDatPath)
	{
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (!File.Exists(scriptDatPath))
			return names;

		try
		{
			if (ScriptDatLanguageNameReader.TryRead(InputFileReader.ReadAllBytes(scriptDatPath), out IReadOnlyList<string> oldNames))
			{
				names.UnionWith(oldNames);
			}
			else
			{
				diagnostics.Warning(
					"The existing Script.dat lists no language files the compiler can read, so only the languages of this script are removed as stale"
				);
			}
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			diagnostics.Warning(
				$"Cannot read the existing Script.dat, so only the languages of this script are removed as stale: {ex.Reason()}",
				exception: ex
			);
		}

		return names;
	}
}
