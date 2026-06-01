using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Collects the language files the <c>File=</c> lines of the <c>[Language]</c> sections list, in index order.
/// </summary>
internal sealed class LanguageSectionBuilder
{
	/// <summary>
	/// The most language files a script lists, because VB6 fails on the ninth (<c>Module1.bas:5515-5519</c>).
	/// </summary>
	private const int MaxLanguageFiles = 8;

	private readonly DiagnosticSink _diagnostics;
	private readonly ExpressionEvaluator _evaluator;
	private readonly List<string> _languageFiles = [];
	private readonly List<SourceLocation> _locations = [];

	internal LanguageSectionBuilder(DiagnosticSink diagnostics, ExpressionEvaluator evaluator)
	{
		_diagnostics = diagnostics;
		_evaluator = evaluator;
	}

	/// <summary>Gets the language files in index order.</summary>
	internal IReadOnlyList<string> LanguageFiles => _languageFiles;

	/// <summary>
	/// Gets the location of the file name of the <c>File=</c> line that lists each language file, in index order.
	/// </summary>
	internal IReadOnlyList<SourceLocation> Locations => _locations;

	/// <summary>
	/// Adds a command line of a <c>[Language]</c> section, which accepts <c>File=</c> alone
	/// (<c>Module1.bas:5475-5521</c>).
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when the line is valid; otherwise, <see langword="false"/>, after an error.
	/// </returns>
	internal bool TryAdd(ScriptCommandLine line)
	{
		if (!line.Command.IgnoreCaseEquals("File="))
		{
			_diagnostics.Error(
				ScriptCommandMessages.UnknownCommand(ScriptSectionHeaders.Of(ScriptSectionType.Language), line.Command),
				line.CommandLocation
			);

			return false;
		}

		if (
			!ScriptArgumentResolver.CheckArgumentCount(
				diagnostics: _diagnostics,
				args: line.Arguments,
				expected: 2,
				command: "File=",
				location: line.CommandLocation
			)
		)
		{
			return false;
		}

		// VB6 reads the index with GetArgNumerico (Module1.bas:5493), so $0, a constant or a sum names it too.
		NumericEvaluation index = _evaluator.Evaluate(line.Arguments[0], line.Location.File);

		if (!index.Success)
		{
			_diagnostics.Error($"The index of the language file is invalid: {index.Error}", line.ArgumentLocation(0));
			return false;
		}

		if (index.Value != _languageFiles.Count)
		{
			_diagnostics.Error(
				FormattableString.Invariant(
					$"Unexpected index {index.Value} for language file; number the File= lines from 0 in order, so this one is {_languageFiles.Count}"
				),

				line.CommandLocation
			);

			return false;
		}

		string fileName = line.Arguments[1];

		if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
		{
			_diagnostics.Error($"The language file '{fileName}' lacks the .txt extension; write the name of a .txt file", line.CommandLocation);
			return false;
		}

		// Script.dat names the language with its folder (Module1.bas:6768), while the .dat is published under its bare name, so the engine
		// could not open it; VB6 fails to copy such a main language (:8968).
		if (fileName.AsSpan().IndexOfAny('\\', '/', ':') >= 0)
		{
			_diagnostics.Error(
				$"The language file '{fileName}' names a folder, so Script.dat would name a language file the engine cannot find; move the file into the script folder and write its name alone",
				line.ArgumentLocation(1)
			);

			return false;
		}

		_languageFiles.Add(fileName);
		_locations.Add(line.ArgumentLocation(1));

		if (_languageFiles.Count > MaxLanguageFiles)
		{
			_diagnostics.Error($"Too many language files; a script lists at most {MaxLanguageFiles}", line.CommandLocation);
			return false;
		}

		return true;
	}
}
