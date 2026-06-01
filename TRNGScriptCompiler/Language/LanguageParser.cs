using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Numerics;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Language;

/// <summary>Parses TRNG language text files into <see cref="LanguageData"/> instances.</summary>
internal sealed class LanguageParser
{
	private readonly CompilerLog _logger;
	private readonly DiagnosticSink _diagnostics;

	internal LanguageParser(CompilerLog logger, DiagnosticSink diagnostics)
	{
		_logger = logger;
		_diagnostics = diagnostics;
	}

	/// <summary>Parses a language <c>.txt</c> file from a host's overlay or from disk.</summary>
	/// <returns>The language, or <see langword="null"/> after an error.</returns>
	internal LanguageData? Parse(string filePath, VirtualFileSystem? overlay, CancellationToken token = default)
	{
		string content;

		try
		{
			content = TextInputReader.ReadAllText(filePath, overlay, _diagnostics);
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			_diagnostics.Error($"Cannot read language file {Path.GetFileName(filePath)}: {ex.Reason()}", SourceLocation.InFile(filePath), ex);
			return null;
		}

		return Parse(filePath, content, token);
	}

	/// <summary>Parses language file content held in memory, naming the file by its full path in diagnostics.</summary>
	/// <returns>The language, or <see langword="null"/> after an error.</returns>
	internal LanguageData? Parse(string filePath, string content, CancellationToken token = default)
	{
		_logger.LogVerbose($"Parsing {Path.GetFileName(filePath)}");

		// VB6 reads a language file with Line Input (Module1.bas:1967, 1992), which stops at a Ctrl-Z and drops what follows it.
		if (!TextFileUtilities.TryCutAtEndOfFileMark(content, out content, out int markLineNumber))
		{
			_diagnostics.Error(TextFileUtilities.TextAfterEndOfFileMarkError, new SourceLocation(markLineNumber, File: filePath));
			return null;
		}

		// Each table keeps its own strings, so a file that skips or reorders a section still fills the table it names.
		ClassicStringTable[] tables = [new(), new(), new()];
		var extraStrings = new List<ExtraNgString>();

		string[] lines = TextFileUtilities.SplitLines(content);

		int currentSection = -1;
		int extraNgStart = -1;

		// VB6 reads the classic sections in one loop, leaves it at [ExtraNG] (Module1.bas:2021-2025) and reads the rest of the file in a
		// loop of its own.
		for (int i = 0; i < lines.Length; i++)
		{
			token.ThrowIfCancellationRequested();

			var location = new SourceLocation(i + 1, File: filePath);
			string normalizedLine = LanguageEscapeCodec.Encode(lines[i]);

			if (TryParseSectionHeader(normalizedLine, out int newSection, out bool isExtraNg))
			{
				if (isExtraNg)
				{
					extraNgStart = i + 1;
					break;
				}

				// VB6 numbers a repeated header as the next table (Module1.bas:2028-2031), which moves every later string.
				if (tables[newSection].HasHeader)
				{
					_diagnostics.Error($"The section header {normalizedLine} appears twice; merge the two sections into one", location);
					return null;
				}

				tables[newSection].HasHeader = true;
				currentSection = newSection;

				continue;
			}

			if (IsMisreadHeader(lines[i], normalizedLine, location))
				return null;

			// VB6's validity gate uses the raw line: only truly empty lines and lines whose
			// first character is ';' are skipped.
			if (normalizedLine.Length == 0 || normalizedLine[0] == ';')
				continue;

			if (currentSection < 0)
			{
				// VB6 drops text above the first header without a word (Module1.bas:2049).
				string trimmed = lines[i].Trim();

				if (trimmed.Length == 0 || trimmed[0] == ';')
					continue;

				_diagnostics.Error(
					$"'{trimmed}' sits above the first section header, so it belongs to no table; move it under a section header, or start it with ';' to make it a comment",
					location
				);

				return null;
			}

			if (!IsWritableClassicString(lines[i], normalizedLine, location))
				return null;

			tables[currentSection].Add(normalizedLine, i + 1);
		}

		if (extraNgStart >= 0 && !TryReadExtraNgSection(filePath, lines, start: extraNgStart, extraStrings, token))
			return null;

		return BuildLanguageData(filePath, tables, extraStrings);
	}

	/// <summary>
	/// Reports an error when a line is a section header in all but its edges or a trailing comment, which VB6 reads
	/// as a string.
	/// </summary>
	/// <param name="rawLine">The line as the file holds it.</param>
	/// <param name="normalizedLine">The line with its special characters escaped, as the diagnostic shows it.</param>
	/// <param name="location">The file and line the diagnostic names.</param>
	/// <returns><see langword="true"/> after an error.</returns>
	private bool IsMisreadHeader(string rawLine, string normalizedLine, SourceLocation location)
	{
		// VB6 compares the line with the header names whole (Module1.bas:2015-2018), where a classic string line keeps its comment until
		// the text is sized (:1997, 2188), so a header followed by a comment is a string and its section never starts.
		int comment = rawLine.IndexOf(';');

		if (comment > 0 && TryParseSectionHeader(rawLine[..comment].Trim(' ', '\t'), out _, out _))
		{
			_diagnostics.Error(
				$"The section header '{normalizedLine}' is followed by a comment, so the section would not start; move the comment to a line of its own",
				location
			);

			return true;
		}

		// A space or a tab at an edge of the header, which VB6 escapes to \t before it compares (:1993), makes a string of the table
		// before it, and every later table moves.
		if (TryParseSectionHeader(rawLine.Trim(' ', '\t'), out _, out _))
		{
			_diagnostics.Error(
				$"The section header '{normalizedLine}' has spaces or tabs around it, so it would count as a string of the section above it; remove them",
				location
			);

			return true;
		}

		return false;
	}

	/// <summary>
	/// Checks a classic string line under a section header, reporting an error when the game would read it wrong,
	/// and warns when it reads in a way its writer may not mean.
	/// </summary>
	private bool IsWritableClassicString(string rawLine, string normalizedLine, SourceLocation location)
	{
		// VB6's gate tests the first character of the line (Module1.bas:2012), where a tab is already the two characters \t (:1993), so an
		// indented comment is a string that moves every later index: an empty one after spaces, a tab after a tab.
		if (rawLine.TrimStart(' ', '\t').StartsWith(';'))
		{
			_diagnostics.Error(
				$"The line '{normalizedLine}' is indented, so it counts as a string rather than a comment; remove the indent to make it a comment, or write a line of spaces for an empty string",
				location
			);

			return false;
		}

		// VB6 stores the RAW (still C-escaped) text; decoding happens only when the
		// .dat file is written, so the parse-time and write-time results stay identical.
		if (!LanguageEscapeCodec.TryDecodeClassic(normalizedLine, out string decoded, out string specialTag))
		{
			_diagnostics.Error(LanguageEscapeCodec.FormatInvalidEscapeError($"The line '{normalizedLine}'"), location);
			return false;
		}

		if (LanguageEscapeCodec.FindStringEnd(decoded) is { } stringEnd)
		{
			_diagnostics.Error(LanguageEscapeCodec.FormatStringEndError(normalizedLine, stringEnd), location);
			return false;
		}

		WarnIfMisreadable(line: normalizedLine, decoded, specialTag, location);
		return true;
	}

	private LanguageData BuildLanguageData(string filePath, ClassicStringTable[] tables, List<ExtraNgString> extraStrings)
	{
		// The strings go out grouped by table, in the order the reader expects (Mod_ScriptDat.bas:185-190), so an index counts from the
		// start of [Strings] whatever order the file lists the sections in.
		List<string> strings = [.. tables[0].Strings, .. tables[1].Strings, .. tables[2].Strings];

		_logger.LogVerbose($"Found {strings.Count} standard strings", indent: 1);
		_logger.LogVerbose($"Found {extraStrings.Count} extra NG strings", indent: 1);

		return new LanguageData
		{
			LanguageFile = filePath,
			Strings = [.. strings],
			StringLines = [.. tables[0].Lines, .. tables[1].Lines, .. tables[2].Lines],
			ExtraStrings = [.. extraStrings],
			TotalSectionStrings = tables[0].Strings.Count,
			TotalPsxStrings = tables[1].Strings.Count,
			TotalPcStrings = tables[2].Strings.Count,
		};
	}

	/// <summary>
	/// Reads the <c>[ExtraNG]</c> lines that follow the header, up to the end sentinel or the end of the file.
	/// </summary>
	/// <remarks>
	/// VB6 reads these lines raw, trims spaces before the <c>;</c> test and reads
	/// nothing past the sentinel (Module1.bas:2082-2092).
	/// </remarks>
	private bool TryReadExtraNgSection(string filePath, string[] lines, int start, List<ExtraNgString> extraStrings, CancellationToken token)
	{
		var firstByIndex = new Dictionary<int, ExtraNgString>();

		for (int i = start; i < lines.Length; i++)
		{
			token.ThrowIfCancellationRequested();

			var location = new SourceLocation(i + 1, File: filePath);
			string line = lines[i].Trim(' ');

			if (line.Length == 0 || line[0] == ';')
				continue;

			if (line.IgnoreCaseEquals("THE LAST STRING ENTRY"))
				return true;

			// VB6 fails on a header here as a line without ':' (Module1.bas:2095-2103).
			if (TryParseSectionHeader(line, out _, out bool isExtraNg))
			{
				_diagnostics.Error(
					isExtraNg
						? $"The section header {line} appears twice; merge the two sections into one"
						: $"The section header {line} sits inside [ExtraNG], which must be the last section; move the section above [ExtraNG]",

					location
				);

				return false;
			}

			int colonIndex = line.IndexOf(':');

			if (colonIndex < 0)
			{
				_diagnostics.Error($"Syntax error in ExtraNG line (missing ':' character): {line}", location);
				return false;
			}

			string indexText = line[..colonIndex].Trim(' ');

			// VB6 tests the index with IsNumeric and reads it with Val (Module1.bas:2109-2118), so &H82 is 130 there and text fails.
			// The port reads one number: decimal, &H or &O as Val does, or $, which Val reads as 0; a sum, a name, # or a fraction fails (PD-51).
			if (!NumericLiteral.TryReadStandalone(indexText, out int index, out string? reason))
			{
				if (!IsSignedDecimal(indexText))
				{
					_diagnostics.Error($"The ExtraNG index '{indexText}' is invalid: {reason}", location);
					return false;
				}

				// A decimal number past 32 bits is out of range.
				index = -1;
			}

			// VB6 fails an index out of range (:2119-2126).
			if (index is < 0 or > 32767)
			{
				_diagnostics.Error($"The ExtraNG index {indexText} is outside 0-32767; use an index in that range", location);
				return false;
			}

			// The text is stored raw, its escapes intact, and decoded when the language file is written.
			var extraString = new ExtraNgString
			{
				Index = index,
				Text = line[(colonIndex + 1)..].Trim(' '),
				Location = location,
			};

			if (!CheckExtraNgText(line, text: extraString.Text, location))
				return false;

			// VB6 writes both entries of one index (:2133-2138), and every lookup finds the first.
			ExtraNgString? earlier = firstByIndex.GetValueOrDefault(extraString.Index);

			if (earlier is not null && !earlier.Text.Equals(extraString.Text, StringComparison.Ordinal))
			{
				_diagnostics.Error(
					$"The ExtraNG index {extraString.Index} holds two texts, '{earlier.Text}' and '{extraString.Text}'; give each text an index of its own",
					location
				);

				return false;
			}

			if (earlier is not null)
				_diagnostics.Warning($"The ExtraNG string {extraString.Index}: {extraString.Text} is listed twice; remove the repeat", location);

			extraStrings.Add(extraString);
			firstByIndex.TryAdd(extraString.Index, extraString);
		}

		return true;
	}

	/// <summary>
	/// Warns about a classic string line that VB6 compiles as written but that reads as something else.
	/// </summary>
	/// <remarks>
	/// The line is stored with its special characters escaped, and the tag keeps its colon or is empty.
	/// </remarks>
	private void WarnIfMisreadable(string line, string decoded, string specialTag, SourceLocation location)
	{
		string trimmed = line.Trim(' ');

		// VB6 removes any leading word that ends in ':' (Module1.bas:2244-2263), while its comment calls a tag upper case (:2168-2169).
		if (specialTag.Length > 0 && !IsUpperCaseTag(specialTag.AsSpan(0, specialTag.Length - 1)))
		{
			_diagnostics.Warning(
				$"'{specialTag}' at the start of '{trimmed}' is removed from the text as a tag; if the text is meant to start with it, put a dummy tag in front, as in 'T:{trimmed}'",
				location
			);
		}
		else if (decoded.Length == 0)
		{
			_diagnostics.Warning($"The line '{line}' counts as an empty string, which takes an index of its own", location);
		}

		if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
			_diagnostics.Warning($"The line '{trimmed}' is not a section header, so it counts as a string", location);
	}

	/// <summary>
	/// Determines whether a tag name is upper-case letters, digits and <c>_</c>, with at least one letter.
	/// </summary>
	private static bool IsUpperCaseTag(ReadOnlySpan<char> name)
	{
		bool hasLetter = false;

		foreach (char character in name)
		{
			if (char.IsAsciiLetterUpper(character))
				hasLetter = true;
			else if (!char.IsAsciiDigit(character) && character != '_')
				return false;
		}

		return hasLetter;
	}

	/// <summary>
	/// Rejects an inline ExtraNG text that holds a character the game reads as the end of the string.
	/// </summary>
	/// <remarks>
	/// The language encoder checks an <c>@file</c> text and reports an escape that does not decode.
	/// </remarks>
	private bool CheckExtraNgText(string line, string text, SourceLocation location)
	{
		if (text.StartsWith('@'))
			return true;

		if (!LanguageEscapeCodec.TryDecodeExtraNg(text, out string decoded) || LanguageEscapeCodec.FindStringEnd(decoded) is not { } stringEnd)
			return true;

		_diagnostics.Error(LanguageEscapeCodec.FormatStringEndError(line, stringEnd), location);
		return false;
	}

	/// <summary>
	/// Reads a classic section header as its zero-based index, or flags <c>[ExtraNG]</c>; other text, even bracketed,
	/// is content.
	/// </summary>
	/// <remarks>
	/// The classic headers are <c>[Strings]</c>, <c>[PSXStrings]</c> and <c>[PCStrings]</c>; the line is C-escaped,
	/// or trimmed in <c>[ExtraNG]</c>.
	/// </remarks>
	private static bool TryParseSectionHeader(string normalizedLine, out int section, out bool isExtraNg)
	{
		section = -1;
		isExtraNg = false;

		if (normalizedLine.IgnoreCaseEquals("[Strings]"))
		{
			section = 0;
			return true;
		}

		if (normalizedLine.IgnoreCaseEquals("[PSXStrings]"))
		{
			section = 1;
			return true;
		}

		if (normalizedLine.IgnoreCaseEquals("[PCStrings]"))
		{
			section = 2;
			return true;
		}

		if (normalizedLine.IgnoreCaseEquals("[ExtraNG]"))
		{
			isExtraNg = true;
			return true;
		}

		return false;
	}

	/// <summary>Tells whether a text is a run of decimal digits with an optional sign.</summary>
	/// <param name="text">The index text, trimmed of spaces.</param>
	private static bool IsSignedDecimal(string text)
	{
		ReadOnlySpan<char> digits = text.AsSpan(text.StartsWith('-') || text.StartsWith('+') ? 1 : 0);
		return !digits.IsEmpty && !digits.ContainsAnyExceptInRange('0', '9');
	}
}
