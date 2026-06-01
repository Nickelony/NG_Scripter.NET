using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Language;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Pipeline;

/// <summary>
/// Parses the language files a script lists, chooses the main language, and checks each against the first.
/// </summary>
/// <remarks>
/// The first language parses on its own before the NG lines compile, because their string arguments resolve against it.
/// </remarks>
internal sealed class LanguageSet
{
	private readonly AnalysisContext _context;

	internal LanguageSet(AnalysisContext context) => _context = context;

	/// <summary>Bundles the outputs of the language-parsing phase.</summary>
	internal sealed record ParseResult(LanguageData FirstLanguageData, IReadOnlyList<LanguageData> AllLanguageData, string MainLanguageFile);

	/// <summary>
	/// Parses a language file, reporting a missing one at the file name on the <c>File=</c> line that lists it.
	/// </summary>
	/// <returns>The language data, or <see langword="null"/> when the file is missing or fails to parse.</returns>
	internal LanguageData? ParseLanguageFile(string languageFile, SourceLocation fileNameLocation)
	{
		string languagePath = GetLanguagePath(languageFile);
		string languageFileName = Path.GetFileName(languagePath);

		if (!VirtualFileSystem.FileExists(languagePath, _context.Overlay))
		{
			_context.Run.Diagnostics.Error($"Missing language file: {languageFileName}", fileNameLocation);
			return null;
		}

		_context.Run.RecordFileRead(languagePath);
		var languageParser = new LanguageParser(_context.Run.Logger, _context.Run.Diagnostics);

		return languageParser.Parse(filePath: languagePath, overlay: _context.Overlay, token: _context.Token);
	}

	/// <summary>
	/// Resolves the main language, parses all language files, and validates main-language ExtraNG completeness.
	/// </summary>
	/// <returns>The language data, or <see langword="null"/> when a fatal error prevents further compilation.</returns>
	internal ParseResult? Parse(ScriptModel scriptModel, string firstLanguageFile, LanguageData firstLanguageData)
	{
		string? mainLanguageFile = ResolveMainLanguageFile(scriptModel.Parsed.LanguageFiles);

		if (mainLanguageFile is null)
			return null;

		List<LanguageData>? allLanguageData = ParseAllLanguages(scriptModel, mainLanguageFile, firstLanguageFile, firstLanguageData);
		return allLanguageData is null ? null : new ParseResult(firstLanguageData, allLanguageData, mainLanguageFile);
	}

	/// <summary>Parses all language files and validates the main language ExtraNG.</summary>
	/// <returns>
	/// The data of every language file in script order, or <see langword="null"/> when a language fails to parse
	/// or to validate.
	/// </returns>
	private List<LanguageData>? ParseAllLanguages(
		ScriptModel scriptModel,
		string mainLanguageFile,
		string firstLanguageFile,
		LanguageData firstLanguageData
	)
	{
		_context.Run.Logger.Log("ANALYZING LANGUAGE FILES ...");

		var allLanguageData = new List<LanguageData> { firstLanguageData };

		for (int index = 0; index < scriptModel.Parsed.LanguageFiles.Count; index++)
		{
			_context.Token.ThrowIfCancellationRequested();

			string languageFile = scriptModel.Parsed.LanguageFiles[index];
			LanguageData? languageData;

			// Only the first entry reuses the first language; a later entry naming the same file is parsed again, so the composition reports the
			// two outputs that share a name (PD-21).
			if (index == 0)
			{
				languageData = firstLanguageData;
			}
			else
			{
				// A missing language fails the compile where VB6 warns and publishes without it (Module1.bas:8891-8894, PD-50).
				languageData = ParseLanguageFile(languageFile, scriptModel.Parsed.LanguageFileLocations[index]);

				if (languageData is null)
					return null;

				allLanguageData.Add(languageData);
			}

			bool isMainLanguage = PathUtilities.HaveSameFileName(languageFile, mainLanguageFile);

			if (!CheckStringCounts(firstLanguageFile, firstLanguageData, languageFile, languageData, isMainLanguage))
				return null;

			if (isMainLanguage && !ValidateMainLanguageExtraNg(firstLanguageData, mainLanguageData: languageData, mainLanguageFile: languageFile))
				return null;
		}

		return allLanguageData;
	}

	private string? ResolveMainLanguageFile(IReadOnlyList<string> languageFiles)
	{
		CompilationRun run = _context.Run;
		MainLanguageSelector? selector = _context.Options.MainLanguage;

		if (selector is null)
		{
			string defaultLanguage = languageFiles[0];
			run.Diagnostics.Warning($"No main language is selected, so the first listed language, {defaultLanguage}, ships with the game");
			return defaultLanguage;
		}

		if (selector.IsIndex)
		{
			int languageIndex = selector.Index;

			if (languageIndex >= 0 && languageIndex < languageFiles.Count)
				return languageFiles[languageIndex];

			run.Diagnostics.Error(
				FormattableString.Invariant(
					$"The main language index {languageIndex} lies outside 0 to {languageFiles.Count - 1}, the indexes of the listed languages {string.Join(", ", languageFiles)}"
				)
			);

			return null;
		}

		foreach (string languageFile in languageFiles)
		{
			if (selector.Matches(languageFile))
				return languageFile;
		}

		run.Diagnostics.Error(
			$"The main language {selector.Text} is not one of the languages Script.txt lists, which are {string.Join(", ", languageFiles)}"
		);

		return null;
	}

	private string GetLanguagePath(string languageFile) => _context.Run.PathResolver.ResolveScriptPath(_context.Paths.ScriptDirectory, languageFile);

	/// <summary>
	/// Compares a language's <c>[Strings]</c> count with the first language's, from which <c>Script.dat</c> takes
	/// its indexes.
	/// </summary>
	/// <returns><see langword="false"/> when the main language's count differs.</returns>
	/// <remarks>
	/// VB6 warns for every language (Module1.bas:8941-8951); a main language that differs fails, because it ships
	/// with the game.
	/// </remarks>
	private bool CheckStringCounts(
		string firstLanguageFile,
		LanguageData firstLanguageData,
		string languageFile,
		LanguageData languageData,
		bool isMainLanguage
	)
	{
		if (
			PathUtilities.HaveSameFileName(firstLanguageFile, languageFile)
			|| languageData.TotalSectionStrings == firstLanguageData.TotalSectionStrings
		)
		{
			return true;
		}

		string message =
			$"{Path.GetFileName(languageFile)} holds {languageData.TotalSectionStrings} [Strings] lines and {Path.GetFileName(firstLanguageFile)} holds "
			+ $"{firstLanguageData.TotalSectionStrings}, so the indexes Script.dat takes from the first language point at other strings in this one; give both files the same [Strings] lines in the same order";

		if (!isMainLanguage)
		{
			_context.Run.Diagnostics.Warning(message, SourceLocation.InFile(GetLanguagePath(languageFile)));
			return true;
		}

		_context.Run.Diagnostics.Error(message, SourceLocation.InFile(GetLanguagePath(languageFile)));
		return false;
	}

	private bool ValidateMainLanguageExtraNg(LanguageData firstLanguageData, LanguageData mainLanguageData, string mainLanguageFile)
	{
		if (ReferenceEquals(firstLanguageData, mainLanguageData))
			return true;

		var mainIndexes = mainLanguageData.ExtraStrings.Select(extraString => extraString.Index).ToHashSet();

		foreach (ExtraNgString extraString in firstLanguageData.ExtraStrings)
		{
			if (!mainIndexes.Contains(extraString.Index))
			{
				string message = $"Main language {Path.GetFileName(mainLanguageFile)} is missing ExtraNG string index {extraString.Index}";
				_context.Run.Diagnostics.Error(message, SourceLocation.InFile(GetLanguagePath(mainLanguageFile)));
				return false;
			}
		}

		return true;
	}
}
