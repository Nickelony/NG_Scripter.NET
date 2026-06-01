using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Verifies the emitted NG header structure: field boundaries must parse, mini-tag sizes must stay inside their field,
/// and the mini-tag count must match what the encoder expected.
/// </summary>
internal static class NgHeaderVerifier
{
	/// <summary>
	/// Verifies the header words against the mini-tag count the encoder expected, reporting a broken structure
	/// as an error.
	/// </summary>
	internal static bool Verify(List<short> words, int expectedMiniTags, DiagnosticSink diagnostics)
	{
		int countedMiniTags = 0;
		int index = 0;

		while (index < words.Count)
		{
			if (!TryReadField(words, index, out int totalWords, out ushort tag, out int dataStart, out int dataCount))
			{
				diagnostics.Error("The NG header is corrupt, because its field boundaries do not parse");
				return false;
			}

			if (totalWords == 0)
			{
				// The zero word that follows an import whose size is 3 mod 4.
				index++;
				continue;
			}

			if (tag is (ushort)NgTagType.ScriptOptions or (ushort)NgTagType.ScriptLevel)
			{
				int localIndex = 0;

				while (localIndex < dataCount)
				{
					ushort miniTagHeader = unchecked((ushort)words[dataStart + localIndex]);
					int miniTagWordCount = miniTagHeader & 0x00FF;

					if (miniTagWordCount == 0)
						break;

					countedMiniTags++;
					localIndex += miniTagWordCount + 1;

					if (localIndex > dataCount)
					{
						diagnostics.Error(
							$"The NG header is corrupt, because the mini-tag 0x{(miniTagHeader >> 8) & 0xFF:X2} runs past the end of its section"
						);

						return false;
					}
				}
			}

			index += totalWords;
		}

		if (countedMiniTags != expectedMiniTags)
		{
			diagnostics.Error($"The NG header holds {countedMiniTags} mini-tags, but {expectedMiniTags} were written");
			return false;
		}

		return true;
	}

	private static bool TryReadField(List<short> words, int index, out int totalWords, out ushort tag, out int dataStart, out int dataCount)
	{
		totalWords = 0;
		tag = 0;
		dataStart = 0;
		dataCount = 0;

		if (index >= words.Count)
			return false;

		ushort firstWord = unchecked((ushort)words[index]);

		if ((firstWord & 0x8000) != 0)
		{
			if (index + 2 >= words.Count)
				return false;

			totalWords = ((firstWord & 0x7FFF) << 16) | unchecked((ushort)words[index + 1]);

			if (totalWords <= 0 || index + totalWords > words.Count)
				return false;

			tag = unchecked((ushort)words[index + 2]);
			dataStart = index + 3;
			dataCount = totalWords - 3;
			return true;
		}

		totalWords = firstWord;

		if (totalWords == 0)
			return true; // zero-word separator - caller should skip

		if (totalWords < 0 || index + totalWords > words.Count || index + 1 >= words.Count)
			return false;

		tag = unchecked((ushort)words[index + 1]);
		dataStart = index + 2;
		dataCount = totalWords - 2;
		return true;
	}
}
