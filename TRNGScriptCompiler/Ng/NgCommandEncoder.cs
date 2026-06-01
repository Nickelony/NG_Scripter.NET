using System.Runtime.InteropServices;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Ng;

/// <summary>Encodes the parsed arguments of one NG command into its binary word payload.</summary>
internal sealed class NgCommandEncoder
{
	private readonly DiagnosticSink _diagnostics;
	private readonly StringReferenceResolver _strings;

	/// <param name="diagnostics">
	/// The per-run sink that receives a fatal diagnostic for each string reference that does not resolve.
	/// </param>
	/// <param name="languageData">The first language, whose indexes the string references resolve to.</param>
	internal NgCommandEncoder(DiagnosticSink diagnostics, LanguageData languageData)
	{
		_diagnostics = diagnostics;
		_strings = new StringReferenceResolver(languageData, diagnostics);
	}

	/// <summary>Finds the index a string reference resolves to, without reporting one that does not resolve.</summary>
	/// <returns>The index, or <c>-1</c> when the reference does not resolve.</returns>
	internal int FindStringIndex(string text) => _strings.Find(text, allowExtraNg: true);

	/// <summary>
	/// Encodes parsed arguments into words: CommandCode * 256 + ArgumentWordCount, then the argument words.
	/// </summary>
	/// <remarks>
	/// A bool command, which only sets option flags, encodes to no words; an unresolved string returns
	/// <see langword="null"/> after an error.
	/// </remarks>
	internal List<short>? Encode(NgCommandDefinition definition, List<NgArgumentValue> commandArgs, SourceLocation location)
	{
		// For bool commands, don't add command bytes - they become flags
		if (definition.IsFlag)
			return [];

		var words = new List<short>
		{
			// Single-pass emission - the argument words are appended once and the
			// header word's count is read back from the buffer, so the declared word count and
			// the emitted words cannot drift apart.
			0,
		};

		for (int i = 0; i < commandArgs.Count; i++)
		{
			NgArgumentValue arg = commandArgs[i];
			NgArgumentType argType = i < definition.ArgumentTypes.Count ? definition.ArgumentTypes[i] : NgArgumentType.Word;

			if (!TryAddArgumentWords(words, arg, type: argType, code: definition.Code, location))
				return null;
		}

		// Add header word (tag * 256 + word count)
		int totalWords = words.Count - 1;
		words[0] = (short)(((int)definition.Code * 256) + totalWords);

		return words;
	}

	/// <summary>
	/// Appends the words for one argument, resolving string indexes through the bound language data when required.
	/// </summary>
	private bool TryAddArgumentWords(List<short> words, NgArgumentValue arg, NgArgumentType type, NgCommandCode code, SourceLocation location)
	{
		switch (type)
		{
			case NgArgumentType.Word:
			case NgArgumentType.Integer:
			case NgArgumentType.ItemSlot:
			case NgArgumentType.Import:
				words.Add((short)arg.Int32Value);
				break;

			case NgArgumentType.Long:
				words.AddDWord(arg.Int32Value);
				break;

			case NgArgumentType.String:
				if (arg.IsDiscard)
				{
					words.Add(-1);
				}
				else
				{
					if (!TryGetStringIndex(text: arg.StringValue, code, location, index: out int strIndex))
						return false;

					words.Add((short)strIndex);
				}

				break;

			case NgArgumentType.Array:
				if (arg.ArrayValue is { } array)
				{
					foreach (NgArgumentValue item in array)
						words.Add((short)item.Int32Value);
				}

				break;

			// VB6 packs a byte or nybble array only when it has an element (Module1.bas:7182-7186), so an empty one writes no count byte.
			case NgArgumentType.ArrayByte:
				if (arg.ArrayValue is { Count: > 0 } byteArray)
				{
					var bytes = new List<byte> { (byte)byteArray.Count };

					foreach (NgArgumentValue item in byteArray)
						bytes.Add((byte)item.Int32Value);

					words.AddPackedBytes(CollectionsMarshal.AsSpan(bytes));
				}

				break;

			case NgArgumentType.ArrayNybble:
				if (arg.ArrayValue is { Count: > 0 } nibbleArray)
				{
					var bytes = new List<byte> { (byte)nibbleArray.Count };

					for (int i = 0; i < nibbleArray.Count; i += 2)
					{
						int low = nibbleArray[i].Int32Value;
						int high = (i + 1 < nibbleArray.Count) ? nibbleArray[i + 1].Int32Value : 0;

						bytes.Add((byte)(low | (high << 4)));
					}

					words.AddPackedBytes(CollectionsMarshal.AsSpan(bytes));
				}

				break;

			case NgArgumentType.ArrayLong:
				if (arg.ArrayValue is { } longs)
				{
					foreach (NgArgumentValue item in longs)
						words.AddDWord(item.Int32Value);
				}

				break;
		}

		return true;
	}

	/// <summary>Resolves a string reference, reporting an error where VB6 fails the command.</summary>
	/// <remarks>
	/// VB6 fails the command on a string that does not resolve (Module1.bas:7525-7530) and on a <c>Plugin=</c> name
	/// that is not ExtraNG (:7532-7537).
	/// </remarks>
	private bool TryGetStringIndex(string text, NgCommandCode code, SourceLocation location, out int index)
	{
		if (
			!_strings.TryResolve(
				text,
				allowExtraNg: true,
				notFoundMessage: $"Cannot find the string '{text}' in the first language file",
				location,
				out index
			)
		)
		{
			return false;
		}

		if (code != NgCommandCode.Plugin || (index & LanguageData.ExtraNgBaseOffset) != 0)
			return true;

		_diagnostics.Error($"The plugin name '{text}' must be an ExtraNG string", location);
		return false;
	}
}
