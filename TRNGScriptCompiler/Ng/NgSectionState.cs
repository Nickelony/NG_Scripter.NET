namespace TRNGScriptCompiler.Ng;

/// <summary>Tracks per-section NG occurrence limits and duplicate keys during parsing.</summary>
internal sealed class NgSectionState
{
	private readonly Dictionary<string, int> _occurrenceCounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, HashSet<string>> _seenDuplicateKeys = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Clears all tracked occurrence and duplicate state for a new section.</summary>
	internal void Reset()
	{
		_occurrenceCounts.Clear();
		_seenDuplicateKeys.Clear();
	}

	/// <summary>Attempts to register another occurrence of the specified command.</summary>
	/// <returns><see langword="true"/> when the occurrence is allowed; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// A <paramref name="maxOccurrences"/> of <c>-1</c> allows any number.
	/// </remarks>
	internal bool TryIncrementOccurrence(string commandName, int maxOccurrences)
	{
		int occurrenceCount = _occurrenceCounts.GetValueOrDefault(commandName);

		if (maxOccurrences != -1 && occurrenceCount >= maxOccurrences)
			return false;

		_occurrenceCounts[commandName] = occurrenceCount + 1;
		return true;
	}

	/// <summary>Attempts to register a duplicate-detection key for the specified command.</summary>
	/// <returns><see langword="true"/> when the key is new; otherwise, <see langword="false"/>.</returns>
	internal bool TryRegisterDuplicateKey(string commandName, string duplicateKey)
	{
		if (!_seenDuplicateKeys.TryGetValue(commandName, out HashSet<string>? seenKeys))
		{
			seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_seenDuplicateKeys[commandName] = seenKeys;
		}

		return seenKeys.Add(duplicateKey);
	}
}
