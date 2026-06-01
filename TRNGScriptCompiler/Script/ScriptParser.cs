using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Reads <c>Script.txt</c> and its includes into a <see cref="ParsedScript"/> of sections, settings and lines,
/// compiling nothing.
/// </summary>
/// <remarks>
/// NG lines stay as written, because VB6 compiles them once the script and first language
/// are read (<c>Module1.bas:5655</c>, <c>8283-8457</c>).
/// </remarks>
internal sealed class ScriptParser
{
	private readonly CompilationRun _run;
	private readonly ExpressionEvaluator _evaluator;
	private readonly DefineRecorder _defineRecorder;
	private readonly IReadOnlySet<string> _ngCommandNames;

	private static readonly string[] s_directives = [ScriptLineText.DefineDirective, "#FIRST_ID", "#INCLUDE"];

	/// <remarks>
	/// The NG command names carry their <c>=</c> and match without regard to case.
	/// </remarks>
	internal ScriptParser(CompilationRun run, ExpressionEvaluator evaluator, IReadOnlySet<string> ngCommandNames)
	{
		_run = run;
		_evaluator = evaluator;
		_defineRecorder = new DefineRecorder(run, evaluator);
		_ngCommandNames = ngCommandNames;
	}

	/// <summary>
	/// Reads the <c>Script.txt</c> at a full path and its includes, or returns <see langword="null"/>
	/// when it does not exist.
	/// </summary>
	/// <remarks>
	/// Reading halts at the first unreadable file, include or continuation, which <see cref="Parse"/> reports
	/// after the earlier lines, in order.
	/// </remarks>
	internal ScriptSource? ReadSource(string scriptPath, string scriptDirectory, VirtualFileSystem? overlay, CancellationToken token)
	{
		if (!VirtualFileSystem.FileExists(scriptPath, overlay))
		{
			_run.Diagnostics.Error($"Cannot find script file: {scriptPath}");
			return null;
		}

		_run.Logger.Log($"Parsing script file: {Path.GetFileName(scriptPath)}");
		token.ThrowIfCancellationRequested();

		var sourceLoader = new ScriptSourceLoader(run: _run, scriptPath, scriptDirectory, overlay);

		var sourceLines = new List<ScriptCommandLine>();
		ScriptReadError? readError = null;

		while (readError is null && sourceLoader.HasPendingFiles)
		{
			if (!sourceLoader.TryReadNext(out ScriptSourceLine? sourceLine, token))
			{
				readError = sourceLoader.Error;
				break;
			}

			string line = ScriptLineText.Normalize(sourceLine.Text);

			if (string.IsNullOrWhiteSpace(line))
				continue;

			if (!ScriptLineText.StartsWithDirective(line, "#INCLUDE"))
			{
				(string command, List<string> arguments) = ScriptLineLexer.ParseCommandLine(line);

				sourceLines.Add(
					new ScriptCommandLine(Text: line, SourceText: sourceLine.SourceText, command, arguments, Location: sourceLine.Location)
				);

				continue;
			}

			if (
				!sourceLoader.TryPushInclude(
					rawDirectiveLine: sourceLine.Text,
					errorMessage: out string? includeError,
					exception: out Exception? includeException,
					token
				)
			)
			{
				readError = new ScriptReadError(
					includeError,

					SourceLocation.ForTerm(
						line: sourceLine.Location.Line,
						text: sourceLine.SourceText,
						term: "#INCLUDE",
						file: sourceLine.Location.File
					),

					includeException
				);
			}
		}

		return new ScriptSource(Lines: sourceLines, Files: sourceLoader.LoadedFiles, PluginLines: CollectPluginLines(sourceLines), readError);
	}

	/// <summary>
	/// Collects a script's sections, settings and command lines, or returns <see langword="null"/> after the first
	/// stopping error.
	/// </summary>
	/// <remarks>
	/// The <c>#DEFINE</c> values of every file are recorded first, so a number on any line can name a define
	/// its file declares.
	/// </remarks>
	internal ParsedScript? Parse(ScriptSource source, string engineDirectoryPath, CancellationToken token)
	{
		// A file included twice is listed twice, and its defines scan once, so its errors show once (PD-85 keys the defines on the full path).
		var scannedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach ((string filePath, IReadOnlyList<string> lines) in source.Files)
		{
			if (scannedFiles.Add(filePath))
				_defineRecorder.Scan(filePath, lines);
		}

		var options = new OptionsSectionBuilder(run: _run, evaluator: _evaluator, ngCommandNames: _ngCommandNames, engineDirectoryPath);
		var languages = new LanguageSectionBuilder(_run.Diagnostics, _evaluator);
		var pcExtensions = new ExtensionsSectionBuilder(_run.Diagnostics, ScriptSectionHeaders.Of(ScriptSectionType.PcExtensions));
		var psxExtensions = new ExtensionsSectionBuilder(_run.Diagnostics, ScriptSectionHeaders.Of(ScriptSectionType.PsxExtensions));
		var sections = new List<ParsedSection>();

		ScriptSectionType currentSection = ScriptSectionType.None;
		LevelSectionBuilder? level = null;
		SourceLocation? optionsHeaderLocation = null;

		foreach (ScriptCommandLine commandLine in source.Lines)
		{
			token.ThrowIfCancellationRequested();

			SourceLocation location = commandLine.Location;
			string line = commandLine.Text;

			if (ScriptLineText.StartsWithDirective(line, ScriptLineText.DefineDirective))
			{
				if (NgLine.IsPluginDefineText(line) && !TryAddPluginDefine(currentSection, level, commandLine))
					return null;

				continue;
			}

			if (ScriptLineText.StartsWithDirective(line, "#FIRST_ID"))
			{
				_run.Logger.LogVerbose($"Ignoring #FIRST_ID directive at line {location.Line}");
				continue;
			}

			if (s_directives.FirstOrDefault(directive => line.StartsWith(directive, StringComparison.OrdinalIgnoreCase)) is { } mistypedDirective)
			{
				ReportMistypedDirective(commandLine, directive: mistypedDirective);
				return null;
			}

			ScriptSectionType newSection = ScriptSectionHeaders.Find(line);

			if (newSection != ScriptSectionType.None)
			{
				if (level is not null)
					sections.Add(level.Build());

				level = newSection is ScriptSectionType.Level or ScriptSectionType.Title
					? new LevelSectionBuilder(
						diagnostics: _run.Diagnostics,
						sectionType: newSection,
						ngCommandNames: _ngCommandNames,
						headerLocation: location
					)
					: null;

				currentSection = newSection;

				if (newSection == ScriptSectionType.Options)
					optionsHeaderLocation ??= location;
				else if (newSection == ScriptSectionType.PcExtensions)
					pcExtensions.AddHeader(location);
				else if (newSection == ScriptSectionType.PsxExtensions)
					psxExtensions.AddHeader(location);

				continue;
			}

			// VB6 warns that a command above the first section is out of context and ignores it (Module1.bas:5272-5276); the port fails,
			// because the line has no effect (PD-55).
			if (currentSection == ScriptSectionType.None)
			{
				ReportLineOutsideAnySection(commandLine);
				return null;
			}

			if (string.IsNullOrEmpty(commandLine.Command))
			{
				ReportMissingCommand(location);
				return null;
			}

			bool added = currentSection switch
			{
				ScriptSectionType.Options => options.TryAdd(commandLine),
				ScriptSectionType.Language => languages.TryAdd(commandLine),
				ScriptSectionType.PcExtensions => pcExtensions.TryAdd(commandLine),
				ScriptSectionType.PsxExtensions => psxExtensions.TryAdd(commandLine),
				_ => level is null || level.TryAdd(commandLine),
			};

			if (!added)
				return null;
		}

		if (source.ReadError is { } readError)
		{
			_run.Diagnostics.Error(readError.Message, readError.Location, readError.Exception);
			return null;
		}

		if (level is not null)
			sections.Add(level.Build());

		// VB6 sizes the level-name offsets with ReDim VetOffset(TotLivelli - 1), which fails with no stored section (Module1.bas:6728).
		if (sections.Count == 0)
		{
			_run.Diagnostics.Error("The script has no [Title] or [Level] section, so the game has no level to start; add a [Level] section");
			return null;
		}

		WarnUnexpectedExtensionCount(ScriptSectionType.PcExtensions, pcExtensions);
		WarnUnexpectedExtensionCount(ScriptSectionType.PsxExtensions, psxExtensions);

		return BuildParsedScript(options, optionsHeaderLocation, sections, languages, psxExtensions, pcExtensions);
	}

	private static ParsedScript BuildParsedScript(
		OptionsSectionBuilder options,
		SourceLocation? optionsHeaderLocation,
		List<ParsedSection> sections,
		LanguageSectionBuilder languages,
		ExtensionsSectionBuilder psxExtensions,
		ExtensionsSectionBuilder pcExtensions
	)
	{
		return new ParsedScript
		{
			Options = options.Options,
			OptionsHeaderLocation = optionsHeaderLocation ?? default,
			Sections = [.. sections],
			LanguageFiles = [.. languages.LanguageFiles],
			LanguageFileLocations = [.. languages.Locations],
			PsxExtensions = [.. psxExtensions.Extensions],
			PsxExtensionCount = psxExtensions.FilledCount,
			PsxExtensionLocations = [.. psxExtensions.Locations],
			PcExtensions = [.. pcExtensions.Extensions],
			PcExtensionCount = pcExtensions.FilledCount,
			PcExtensionLocations = [.. pcExtensions.Locations],
			ImportFiles = [.. options.ImportFiles],
			OptionsNgLines = [.. options.NgLines],
		};
	}

	/// <summary>
	/// Collects every <c>Plugin=</c> line up front, so a plugin's constants and exported triggers find its ID
	/// wherever it stands.
	/// </summary>
	/// <remarks>
	/// VB6 builds its plugin table from every <c>Plugin=</c> line of script.txt
	/// before it compiles (<c>Module1.bas:3868</c>, <c>15024-15104</c>).
	/// </remarks>
	private static List<NgLine> CollectPluginLines(List<ScriptCommandLine> commandLines)
	{
		var pluginLines = new List<NgLine>();

		foreach (ScriptCommandLine commandLine in commandLines)
		{
			if (commandLine.Command.IgnoreCaseEquals("Plugin="))
				pluginLines.Add(new NgLine(commandLine));
		}

		return pluginLines;
	}

	/// <summary>
	/// Adds a <c>#DEFINE @plugin</c> line to its <c>[Level]</c> or <c>[Title]</c> section, and fails it anywhere
	/// else (PD-55).
	/// </summary>
	/// <remarks>
	/// VB6 reads the line by its place among the NG commands of a <c>[Level]</c> or <c>[Title]</c>
	/// (<c>Module1.bas:5283-5289</c>).
	/// </remarks>
	private bool TryAddPluginDefine(ScriptSectionType currentSection, LevelSectionBuilder? level, ScriptCommandLine line)
	{
		// A line before the first section fails like any other command line there (PD-55).
		if (currentSection == ScriptSectionType.None)
		{
			ReportLineOutsideAnySection(line);
			return false;
		}

		// VB6 reads the line as the unknown command #define, which fails (Module1.bas:5433-5440, 5481-5485, 5527-5535).
		if (level is null)
		{
			_run.Diagnostics.Error(
				$"A #DEFINE @plugin line names the plugin of the exported triggers that follow it in a [Level] or [Title] section, and has no effect in {ScriptSectionHeaders.Of(currentSection)}; move it into the section of its triggers",

				SourceLocation.ForTerm(
					line: line.Location.Line,
					text: line.SourceText,
					term: ScriptLineText.DefineDirective,
					file: line.Location.File
				)
			);

			return false;
		}

		level.AddPluginDefine(line.Text, line.SourceText, line.Location);
		return true;
	}

	private void ReportLineOutsideAnySection(ScriptCommandLine line)
	{
		// The first word of the normalized line is the first word of the source line.
		string firstWord = line.Text.Split(' ')[0];

		_run.Diagnostics.Error(
			$"The line '{line.Text}' stands above the first section, where it has no effect; move it into the section it belongs to",
			SourceLocation.ForTerm(line: line.Location.Line, text: line.SourceText, term: firstWord, file: line.Location.File)
		);
	}

	private void ReportMissingCommand(SourceLocation location) =>
		_run.Diagnostics.Error("Syntax error: missing command, because the line has no '=' character", location);

	/// <summary>
	/// Fails a word such as <c>#INCLUDEX</c> that extends a directive, in a section and above the first
	/// one alike (PD-57).
	/// </summary>
	/// <remarks>
	/// VB6 reads it as a command, which fails in a section (<c>Module1.bas:5293-5299</c>) and only warns
	/// above the first one (<c>:5272-5276</c>).
	/// </remarks>
	private void ReportMistypedDirective(ScriptCommandLine line, string directive)
	{
		string word = line.Text.Split(' ')[0];

		_run.Diagnostics.Error(
			$"{word} is not a directive; write {directive} followed by a space",
			SourceLocation.ForTerm(line: line.Location.Line, text: line.SourceText, term: word, file: line.Location.File)
		);
	}

	private void WarnUnexpectedExtensionCount(ScriptSectionType sectionType, ExtensionsSectionBuilder extensions)
	{
		if (extensions.FilledCount != ParsedScript.ExtensionSlotCount)
		{
			_run.Diagnostics.Warning(
				$"Unexpected number of extensions in {ScriptSectionHeaders.Of(sectionType)} section: {extensions.FilledCount}",
				extensions.HeaderLocation
			);
		}
	}
}
