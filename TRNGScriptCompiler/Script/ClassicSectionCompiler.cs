using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Compiles the classic commands of each <c>[Level]</c> and <c>[Title]</c> section into the section's bytes.
/// </summary>
/// <remarks>
/// Every section compiles, and a failing command does not stop the ones after it, so one compile reports every classic
/// error of the script.
/// </remarks>
internal sealed class ClassicSectionCompiler
{
	private readonly CompilationRun _run;
	private readonly ScriptArgumentResolver _resolver;
	private readonly ClassicCommandEncoder _encoder;
	private readonly ClassicRepeatChecker _repeats;

	private CompilerLog Logger => _run.Logger;

	/// <remarks>
	/// The string references resolve to the indexes of <paramref name="firstLanguage"/>.
	/// </remarks>
	internal ClassicSectionCompiler(CompilationRun run, ExpressionEvaluator evaluator, LanguageData firstLanguage)
	{
		_run = run;
		_resolver = new ScriptArgumentResolver(run.Diagnostics, evaluator, firstLanguage);
		_encoder = new ClassicCommandEncoder(run.Diagnostics, _resolver);
		_repeats = new ClassicRepeatChecker(run.Diagnostics);
	}

	/// <summary>Compiles each section to its bytes, in script order.</summary>
	/// <returns>The bytes, or <see langword="null"/> when any section reported an error.</returns>
	internal IReadOnlyList<byte[]>? Compile(IReadOnlyList<ParsedSection> sections)
	{
		Logger.LogVerbose("Compiling script sections...");

		var compiled = new List<byte[]>(sections.Count);
		bool success = true;

		for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
		{
			List<byte>? bytes = CompileSection(sections[sectionIndex], sectionIndex);

			if (bytes is null)
				success = false;
			else
				compiled.Add([.. bytes]);
		}

		return success ? compiled : null;
	}

	private List<byte>? CompileSection(ParsedSection section, int sectionIndex)
	{
		var compiledBytes = new List<byte>();

		// The section header starts with no flags and CD 0; commands update both while compiling.
		var sectionFlags = default(ScriptLevelFlags);

		int cdSlot = 0;
		bool success = true;

		// Number the lines as written, because the emission sort below reorders them while the repeat check
		// and the section header still read the order of the file.
		List<ParsedClassicLine> parsedLines = IndexSectionLines(section);
		List<ParsedClassicLine> sectionLines = OrderByEmissionPriority(parsedLines);

		var occurrences = new List<ClassicOccurrence>(sectionLines.Count);

		foreach (ParsedClassicLine line in sectionLines)
		{
			int start = compiledBytes.Count;
			ScriptCommandLine sectionLine = line.CommandLine;

			var context = new ClassicCompileContext(bytes: compiledBytes, cdSlot, line: sectionLine);

			if (!_encoder.TryEncode(context, sectionLine.Arguments))
			{
				// A handler can append part of its bytes before it fails; the section is discarded, and the lines after it still compile.
				compiledBytes.RemoveRange(start, compiledBytes.Count - start);
				success = false;
				continue;
			}

			cdSlot = context.CdSlot;
			sectionFlags |= context.Flags;
			occurrences.Add(ClassicRepeatChecker.GetOccurrence(line, compiledBytes, start, lineFlags: context.Flags));
		}

		success &= _repeats.TryCheck(section, occurrences);
		success &= TryValidateAndWriteSectionHeader(compiledBytes, section, sectionIndex, sectionFlags, cd: cdSlot, parsedLines);

		return success ? compiledBytes : null;
	}

	/// <summary>Pairs each line of a section with its place as written.</summary>
	private static List<ParsedClassicLine> IndexSectionLines(ParsedSection section)
	{
		var parsedLines = new List<ParsedClassicLine>(section.ClassicLines.Count);

		foreach (ScriptCommandLine sectionLine in section.ClassicLines)
			parsedLines.Add(new ParsedClassicLine(CommandLine: sectionLine, SourceIndex: parsedLines.Count));

		return parsedLines;
	}

	/// <summary>
	/// Orders a section's lines with VB6's exchange sort (<c>Module1.bas:5711-5733</c>), whose order the engine reads.
	/// </summary>
	/// <remarks>
	/// Only inventory keys are unique and the sort swaps only on a greater key, so lines sharing a key reverse
	/// when a lower-key line follows them.
	/// </remarks>
	private List<ParsedClassicLine> OrderByEmissionPriority(List<ParsedClassicLine> parsedLines)
	{
		(ParsedClassicLine line, int orderValue)[] keyed = [.. parsedLines.Select(line => (line, GetSortKey(line)))];

		for (int j = 0; j < keyed.Length - 1; j++)
		{
			for (int z = j + 1; z < keyed.Length; z++)
			{
				if (keyed[j].orderValue > keyed[z].orderValue)
					(keyed[j], keyed[z]) = (keyed[z], keyed[j]);
			}
		}

		return [.. keyed.Select(x => x.line)];
	}

	/// <summary>
	/// Computes VB6's sort key of a line, its command's place in VB6's order table times 1000
	/// (<c>Module1.bas:5685-5707</c>).
	/// </summary>
	/// <remarks>
	/// An inventory command, the entries 1 to 7 of the table, adds its item number times 10, and a combo adds its piece
	/// number too.
	/// </remarks>
	private int GetSortKey(ParsedClassicLine line)
	{
		// A command outside VB6's order table keeps key 0.
		if (!ClassicCommandCatalog.TryGet(line.CommandLine.Command, out ClassicCommandInfo? command))
			return 0;

		int key = command.SortOrder * 1000;

		if (command.Inventory != ClassicInventoryKind.None)
			key += ReadSortNumber(line, 0) * 10;

		if (command.Inventory == ClassicInventoryKind.Combo)
			key += ReadSortNumber(line, 1);

		return key;
	}

	/// <summary>
	/// Reads an inventory argument of the sort key, or 0 when it is missing, fails to evaluate or is negative.
	/// </summary>
	private int ReadSortNumber(ParsedClassicLine line, int index)
	{
		ScriptCommandLine commandLine = line.CommandLine;
		IReadOnlyList<string> arguments = commandLine.Arguments;

		return arguments.Count > index && _resolver.TryEvaluate(arguments[index], commandLine.Location.File, out int value) && value >= 0 ? value : 0;
	}

	private static ScriptCommandLine? FindLastLine(List<ParsedClassicLine> parsedLines, string command)
	{
		for (int i = parsedLines.Count - 1; i >= 0; i--)
		{
			if (parsedLines[i].CommandLine.Command.IgnoreCaseEquals(command))
				return parsedLines[i].CommandLine;
		}

		return null;
	}

	/// <summary>
	/// Resolves a <c>Name=</c> string to its language index, or <c>-1</c> when it is missing or a written index
	/// exceeds 255.
	/// </summary>
	/// <remarks>
	/// A <c>[Level]</c> header writes the index in one byte, while VB6 looks up a <c>[Title]</c> name
	/// without writing it.
	/// </remarks>
	private int ResolveLevelNameIndex(string name, SourceLocation location, bool writesIndex)
	{
		int index = _resolver.ResolveStringIndex(name, notFoundMessage: $"Cannot find level name in language file: {name}", location);

		if (index < 0)
			return -1;

		return !writesIndex || _resolver.ValidateOneByteStringIndex(index, text: name, command: ClassicCommandCatalog.NameCommand, location)
			? index
			: -1;
	}

	private bool TryValidateAndWriteSectionHeader(
		List<byte> compiledBytes,
		ParsedSection section,
		int sectionIndex,
		ScriptLevelFlags sectionFlags,
		int cd,
		List<ParsedClassicLine> parsedLines
	)
	{
		bool hasLoadCamera = FindLastLine(parsedLines, ClassicCommandCatalog.LoadCameraCommand) is not null;

		ScriptCommandLine? levelLine = FindLastLine(parsedLines, ClassicCommandCatalog.LevelCommand);
		ScriptCommandLine? nameLine = FindLastLine(parsedLines, ClassicCommandCatalog.NameCommand);

		SourceLocation nameLocation = nameLine is null ? section.HeaderLocation : nameLine.Location;
		bool success = true;

		// VB6 requires LoadCamera= in every stored Level/Title section (Module1.bas:6423-6429).
		if (!hasLoadCamera)
		{
			_run.Diagnostics.Error(
				string.IsNullOrEmpty(section.LevelName)
					? $"LoadCamera= command is required in the [{section.SectionType}] section"
					: $"LoadCamera= command is required in {section.SectionType.ToString().ToLowerInvariant()} section '{section.LevelName}'",

				section.HeaderLocation
			);

			success = false;
		}

		if (string.IsNullOrEmpty(section.LevelPath))
		{
			if (levelLine is null)
				_run.Diagnostics.Error($"Missing 'Level=' command in [{section.SectionType}] section", section.HeaderLocation);
			else
				_run.Diagnostics.Error("'Level=' gives an empty level path; write the path of the level file before the comma", levelLine.Location);

			success = false;
		}

		// VB6 checks a [Level] for a missing Name= (Module1.bas:5839-5843), but a section's name starts as 0 (:5250), so the check never
		// fires and VB6 looks up the string 0; the port fails a level without a name (PD-05).
		if (section.SectionType == ScriptSectionType.Level && string.IsNullOrEmpty(section.LevelName))
		{
			if (nameLine is null)
				_run.Diagnostics.Error("Missing 'Name=' command in [Level] section", section.HeaderLocation);
			else
				_run.Diagnostics.Error("'Name=' gives an empty level name; write the name of the level", nameLocation);

			return false;
		}

		// A [Title] name is looked up and not written (Module1.bas:5995-6009; see ResolveLevelNameIndex).
		if (section.SectionType == ScriptSectionType.Title)
		{
			if (!string.IsNullOrEmpty(section.LevelName) && ResolveLevelNameIndex(section.LevelName, nameLocation, writesIndex: false) < 0)
				return false;

			compiledBytes.Add(0x82);
		}
		else
		{
			int nameIndex = ResolveLevelNameIndex(section.LevelName, nameLocation, writesIndex: true);

			if (nameIndex < 0)
				return false;

			compiledBytes.Add(0x81);
			compiledBytes.Add((byte)nameIndex);
		}

		BinaryUtilities.WriteWord(compiledBytes, (short)sectionFlags);

		compiledBytes.Add((byte)sectionIndex);
		compiledBytes.Add((byte)cd);
		compiledBytes.Add(0x83);

		return success;
	}
}
