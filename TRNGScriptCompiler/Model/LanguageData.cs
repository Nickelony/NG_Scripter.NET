using System.Collections.Frozen;
using System.Globalization;
using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Model;

/// <summary>Holds the strings of one parsed language file, the classic ones and the ExtraNG ones.</summary>
/// <remarks>
/// The class finds a string by its text or resolves a reference to its compiled index; the compiled index of an ExtraNG
/// string adds <see cref="ExtraNgBaseOffset"/> to its own index.
/// </remarks>
internal sealed class LanguageData
{
	/// <summary>Gets the number of <c>[Strings]</c> lines, which open <see cref="Strings"/>.</summary>
	internal required int TotalSectionStrings { get; init; }

	/// <summary>
	/// Gets the number of <c>[PSXStrings]</c> lines, which follow the <c>[Strings]</c> lines in <see cref="Strings"/>.
	/// </summary>
	internal required int TotalPsxStrings { get; init; }

	/// <summary>Gets the number of <c>[PCStrings]</c> lines, which close <see cref="Strings"/>.</summary>
	internal required int TotalPcStrings { get; init; }

	/// <summary>
	/// Gets the classic strings in compiled order, as the language file writes them, with their escapes intact.
	/// </summary>
	internal required IReadOnlyList<string> Strings { get; init; }

	/// <summary>
	/// Gets the 1-based line of each classic string in <see cref="LanguageFile"/>, in the order
	/// of <see cref="Strings"/>.
	/// </summary>
	/// <remarks>
	/// Empty for strings read from no file, whose diagnostics then name the file alone.
	/// </remarks>
	internal IReadOnlyList<int> StringLines { get; init; } = [];

	internal required IReadOnlyList<ExtraNgString> ExtraStrings { get; init; }

	internal required string LanguageFile { get; init; }

	/// <summary>
	/// The base offset added to ExtraNG string indices to distinguish them from classic string indices in the compiled
	/// binary format.
	/// </summary>
	internal const int ExtraNgBaseOffset = 32768;

	private FrozenDictionary<string, int>? _stringIndex;
	private FrozenDictionary<string, int>? _extraStringIndex;

	private Dictionary<string, List<(int Index, string Line)>>? _normalizedStringIndex;
	private Dictionary<string, List<(int Index, string Line)>>? _normalizedExtraStringIndex;

	/// <summary>Finds the index of the first string that matches the text, ordinally without regard to case.</summary>
	/// <returns>The zero-based string index, or <c>-1</c> when the text is not present.</returns>
	internal int FindStringIndex(string text)
	{
		_stringIndex ??= BuildStringIndex();
		return _stringIndex.TryGetValue(text, out int index) ? index : -1;
	}

	/// <summary>Finds the first extra NG string that matches the text, ordinally without regard to case.</summary>
	/// <returns><c>32768 + Index</c> when found; otherwise, <c>-1</c>.</returns>
	internal int FindExtraStringIndex(string text)
	{
		_extraStringIndex ??= BuildIndex(ExtraStrings, e => e.Text, (e, _) => e.Index);
		return _extraStringIndex.TryGetValue(text, out int index) ? ExtraNgBaseOffset + index : -1;
	}

	private FrozenDictionary<string, int> BuildStringIndex() => BuildIndex(Strings, s => s, (_, i) => i);

	private static FrozenDictionary<string, int> BuildIndex<T>(IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, int, int> valueSelector)
	{
		var dict = new Dictionary<string, int>(items.Count, StringComparer.OrdinalIgnoreCase);

		for (int i = 0; i < items.Count; i++)
		{
			// VB6 (Option Compare Text) matches case-insensitively and returns the FIRST match.
			if (!dict.ContainsKey(keySelector(items[i])))
				dict[keySelector(items[i])] = valueSelector(items[i], i);
		}

		return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Resolves a string reference, a <c>!NNN</c> NG index or a text <see cref="FindStringIndex"/> finds,
	/// to its compiled index.
	/// </summary>
	/// <returns>The resolved string index, or <c>-1</c> when not found.</returns>
	/// <remarks>
	/// Without <paramref name="allowExtraNgText"/>, a <c>!NNN</c> index never resolves and a text the classic lookup
	/// misses is not searched among the ExtraNG strings.
	/// </remarks>
	internal int ResolveStringIndex(string text, bool allowExtraNgText = true)
	{
		if (text.StartsWith('!'))
			return ResolveExtraNgIndex(text, allowExtraNgText);

		int index = FindStringIndex(text);

		if (index >= 0)
			return index;

		if (allowExtraNgText)
		{
			index = FindExtraStringIndex(text);

			if (index >= 0)
				return index;
		}

		return -1;
	}

	/// <summary>
	/// Finds, in table order, the strings whose language line, normalized as a script line is, equals the normalized
	/// text (PD-63).
	/// </summary>
	/// <remarks>
	/// Lines differing only in case count as one match, the first, and <paramref name="allowExtraNgText"/> adds
	/// the ExtraNG texts. The first call indexes the normalized lines, so a call costs one lookup.
	/// </remarks>
	internal List<(int Index, string Line)> FindNormalizedMatches(string text, bool allowExtraNgText)
	{
		var matches = new List<(int Index, string Line)>();
		string key = ScriptLineText.Normalize(text);

		// A !NNN reference names an index, not a text.
		if (key.Length == 0 || text.StartsWith('!'))
			return matches;

		_normalizedStringIndex ??= BuildNormalizedIndex(Strings, line => line, (_, i) => i);

		if (_normalizedStringIndex.TryGetValue(key, out List<(int Index, string Line)>? strings))
			matches.AddRange(strings);

		if (allowExtraNgText)
		{
			_normalizedExtraStringIndex ??= BuildNormalizedIndex(ExtraStrings, extra => extra.Text, (extra, _) => ExtraNgBaseOffset + extra.Index);

			if (_normalizedExtraStringIndex.TryGetValue(key, out List<(int Index, string Line)>? extraStrings))
			{
				foreach ((int index, string line) in extraStrings)
					AddUnlessSeen(matches, index, line);
			}
		}

		return matches;
	}

	/// <summary>
	/// Groups the lines by their normalized text, each group in table order, keeping the first of the lines that differ
	/// only in case.
	/// </summary>
	private static Dictionary<string, List<(int Index, string Line)>> BuildNormalizedIndex<T>(
		IReadOnlyList<T> items,
		Func<T, string> lineSelector,
		Func<T, int, int> indexSelector
	)
	{
		var index = new Dictionary<string, List<(int Index, string Line)>>(items.Count, StringComparer.OrdinalIgnoreCase);

		for (int i = 0; i < items.Count; i++)
		{
			string line = lineSelector(items[i]);
			string key = ScriptLineText.Normalize(line);

			if (!index.TryGetValue(key, out List<(int Index, string Line)>? group))
			{
				group = [];
				index.Add(key, group);
			}

			AddUnlessSeen(group, indexSelector(items[i], i), line);
		}

		return index;
	}

	private static void AddUnlessSeen(List<(int Index, string Line)> matches, int index, string line)
	{
		if (!matches.Exists(match => match.Line.Equals(line, StringComparison.OrdinalIgnoreCase)))
			matches.Add((index, line));
	}

	/// <summary>Resolves a <c>!NNN</c> ExtraNG reference.</summary>
	/// <remarks>
	/// VB6 requires the caller to allow ExtraNG references (classic commands may not use them), bounds the index
	/// to 0-32767 and verifies that the requested index actually exists in the ExtraNG table.
	/// </remarks>
	private int ResolveExtraNgIndex(string text, bool allowExtraNgText)
	{
		if (!allowExtraNgText)
			return -1;

		if (
			!int.TryParse(s: text.AsSpan(1), style: NumberStyles.Integer, provider: CultureInfo.InvariantCulture, result: out int ngIndex)
			|| ngIndex is < 0 or > 32767
		)
		{
			return -1;
		}

		foreach (ExtraNgString extraString in ExtraStrings)
		{
			if (extraString.Index == ngIndex)
				return ExtraNgBaseOffset + ngIndex;
		}

		return -1;
	}
}
