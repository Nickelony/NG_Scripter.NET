using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Encodes the classic <c>Script.dat</c> payload from the compiled sections, before NG data is appended.
/// </summary>
internal sealed class ScriptDatEncoder
{
	/// <summary>
	/// The size in bytes of the field that holds the extensions of one platform, their terminators included.
	/// </summary>
	internal const int ExtensionFieldSize = 20;

	private readonly CompilerLog _logger;
	private readonly DiagnosticSink _diagnostics;

	internal ScriptDatEncoder(CompilerLog logger, DiagnosticSink diagnostics)
	{
		_logger = logger;
		_diagnostics = diagnostics;
	}

	/// <summary>
	/// Encodes the classic script payload from the bytes of each section, in the order
	/// of <see cref="ParsedScript.Sections"/>.
	/// </summary>
	internal bool TryEncode(ScriptModel scriptModel, IReadOnlyList<byte[]> sectionBytes, out byte[] bytes)
	{
		bytes = [];

		_logger.LogVerbose("Writing Script.dat header...");

		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream, Vb6Ansi.Encoding, leaveOpen: true);

		if (!WriteClassicPayload(writer, scriptModel, sectionBytes))
			return false;

		writer.Flush();
		bytes = stream.ToArray();
		return true;
	}

	private bool WriteClassicPayload(BinaryWriter writer, ScriptModel scriptModel, IReadOnlyList<byte[]> sectionBytes)
	{
		if (scriptModel.Parsed.Sections.Count > byte.MaxValue)
		{
			_diagnostics.Error(
				$"Too many script sections ({scriptModel.Parsed.Sections.Count}); the format supports at most {byte.MaxValue}",
				scriptModel.Parsed.Sections[byte.MaxValue].HeaderLocation
			);

			return false;
		}

		writer.Write((int)scriptModel.Parsed.Options.Flags);
		writer.Write(scriptModel.Parsed.Options.InputTimeOut);
		writer.Write((byte)scriptModel.Parsed.Options.Security);

		IReadOnlyList<string> levelPaths = scriptModel.Parsed.LevelPaths;

		writer.Write((byte)scriptModel.Parsed.Sections.Count);
		writer.Write((short)levelPaths.Count);

		_logger.LogVerbose($"Number of Levels: {levelPaths.Count}", indent: 1);

		int levelNamesSize = levelPaths.Sum(path => Vb6Ansi.GetByteCount(path) + 1);

		if (levelNamesSize > short.MaxValue)
		{
			_diagnostics.Error(
				$"Level names table is too large ({levelNamesSize} bytes); the format supports at most {short.MaxValue} (VB6 errored on overflow)",

				FindSectionPastLimit(
					scriptModel.Parsed.Sections,

					index =>
						string.IsNullOrEmpty(scriptModel.Parsed.Sections[index].LevelPath)
							? 0
							: Vb6Ansi.GetByteCount(scriptModel.Parsed.Sections[index].LevelPath) + 1
				)
			);

			return false;
		}

		writer.Write((short)levelNamesSize);

		int sectionsSize = sectionBytes.Sum(b => b.Length);

		if (sectionsSize > short.MaxValue)
		{
			_diagnostics.Error(
				$"Section data is too large ({sectionsSize} bytes); the format supports at most {short.MaxValue} (VB6 errored on overflow)",
				FindSectionPastLimit(scriptModel.Parsed.Sections, index => sectionBytes[index].Length)
			);

			return false;
		}

		writer.Write((short)sectionsSize);

		if (
			!TryWriteExtensions(
				writer,
				sectionName: ScriptSectionHeaders.Of(ScriptSectionType.PsxExtensions),
				extensions: scriptModel.Parsed.PsxExtensions,
				slotCount: scriptModel.Parsed.PsxExtensionCount,
				locations: scriptModel.Parsed.PsxExtensionLocations
			)
			|| !TryWriteExtensions(
				writer,
				sectionName: ScriptSectionHeaders.Of(ScriptSectionType.PcExtensions),
				extensions: scriptModel.Parsed.PcExtensions,
				slotCount: scriptModel.Parsed.PcExtensionCount,
				locations: scriptModel.Parsed.PcExtensionLocations
			)
		)
		{
			return false;
		}

		// Such a character needs a byte-order mark (see Vb6Ansi.IsLossy).
		if (scriptModel.Parsed.Sections.FirstOrDefault(section => Vb6Ansi.IsLossy(section.LevelPath)) is { } lossySection)
		{
			_diagnostics.Error(Vb6Ansi.FormatLossyError(lossySection.LevelPath), lossySection.LevelPathLocation);
			return false;
		}

		WriteLevelPathTable(writer, levelPaths);
		WriteSectionTable(writer, sectionBytes);

		return TryWriteLanguageNames(writer, scriptModel);
	}

	/// <summary>Writes the <c>.DAT</c> name of each language, or fails on a name Windows-1252 cannot hold.</summary>
	private bool TryWriteLanguageNames(BinaryWriter writer, ScriptModel scriptModel)
	{
		for (int index = 0; index < scriptModel.Parsed.LanguageFiles.Count; index++)
		{
			string langFile = scriptModel.Parsed.LanguageFiles[index];

			if (Vb6Ansi.IsLossy(langFile))
			{
				_diagnostics.Error(Vb6Ansi.FormatLossyError(langFile), LocationOf(scriptModel.Parsed.LanguageFileLocations, index));
				return false;
			}

			string datName = Path.ChangeExtension(langFile, ".DAT"); // Extension must be uppercase to match VB6 binary output
			BinaryUtilities.WriteNullTerminatedString(writer, datName);
		}

		return true;
	}

	/// <summary>
	/// Writes the level path table, whose size the caller checked fits in 16 bits, so no offset into it overflows.
	/// </summary>
	private void WriteLevelPathTable(BinaryWriter writer, IReadOnlyList<string> levelPaths)
	{
		var levelOffsets = new List<short>();
		int offset = 0;

		foreach (string levelPath in levelPaths)
		{
			levelOffsets.Add((short)offset);
			offset += Vb6Ansi.GetByteCount(levelPath) + 1;
		}

		foreach (short levelOffset in levelOffsets)
			writer.Write(levelOffset);

		foreach (string levelPath in levelPaths)
		{
			_logger.LogVerbose($"Level: \"{levelPath}\"", indent: 1);
			BinaryUtilities.WriteNullTerminatedString(writer, levelPath);
		}
	}

	/// <summary>
	/// Writes the section table, whose sections the caller checked fit in 16 bits, so no offset into them overflows.
	/// </summary>
	private static void WriteSectionTable(BinaryWriter writer, IReadOnlyList<byte[]> sectionBytes)
	{
		int offset = 0;

		foreach (byte[] compiled in sectionBytes)
		{
			writer.Write((short)offset);
			offset += compiled.Length;
		}

		foreach (byte[] compiled in sectionBytes)
			writer.Write(compiled);
	}

	/// <summary>Finds the header of the section whose size carries the running total past the 16-bit limit.</summary>
	private static SourceLocation FindSectionPastLimit(IReadOnlyList<ParsedSection> sections, Func<int, int> sizeOf)
	{
		int total = 0;

		for (int index = 0; index < sections.Count; index++)
		{
			total += sizeOf(index);

			if (total > short.MaxValue)
				return sections[index].HeaderLocation;
		}

		return default;
	}

	private bool TryWriteExtensions(
		BinaryWriter writer,
		string sectionName,
		IReadOnlyList<string?> extensions,
		int slotCount,
		IReadOnlyList<SourceLocation> locations
	)
	{
		// VB6 writes slots 0 to count-1, where the count is the number of distinct commands (Module1.bas:5545-5562, 6697-6725), so a
		// section with a gap drops its highest slot (PD-57).
		for (int i = slotCount; i < extensions.Count; i++)
		{
			if (!string.IsNullOrEmpty(extensions[i]))
			{
				// The count is the number of declared slots, so a slot at or above it leaves an empty slot below it.
				int missingIndex = 0;

				while (!string.IsNullOrEmpty(extensions[missingIndex]))
					missingIndex++;

				string missing = ExtensionsSectionBuilder.SlotCommands[missingIndex];

				_diagnostics.Error(
					$"The {sectionName} section declares '{ExtensionsSectionBuilder.SlotCommands[i]}' but no '{missing}', so the slot of '{ExtensionsSectionBuilder.SlotCommands[i]}' would be dropped; add a '{missing}' line",
					LocationOf(locations, i)
				);

				return false;
			}
		}

		var buffer = new List<byte>();
		int? overflowSlot = null;

		for (int i = 0; i < slotCount && i < extensions.Count; i++)
		{
			string? ext = extensions[i];

			if (!string.IsNullOrEmpty(ext))
			{
				// Such a character needs a byte-order mark (see Vb6Ansi.IsLossy).
				if (Vb6Ansi.IsLossy(ext))
				{
					_diagnostics.Error(Vb6Ansi.FormatLossyError(ext), LocationOf(locations, i));
					return false;
				}

				buffer.AddRange(Vb6Ansi.GetBytes(ext));
			}

			buffer.Add(0);

			if (buffer.Count > ExtensionFieldSize)
				overflowSlot ??= i;
		}

		// VB6 writes a longer list unchecked, which moves every field after it (PD-26). The error points at the slot that carries the list
		// past its field.
		if (overflowSlot is { } slot)
		{
			_diagnostics.Error(
				$"The {sectionName} extension list takes {buffer.Count} bytes with its terminators, but its field holds {ExtensionFieldSize}; shorten the extensions",
				LocationOf(locations, slot)
			);

			return false;
		}

		while (buffer.Count < ExtensionFieldSize)
			buffer.Add(0);

		writer.Write(buffer.ToArray());
		return true;
	}

	private static SourceLocation LocationOf(IReadOnlyList<SourceLocation> locations, int index) =>
		index < locations.Count ? locations[index] : default;
}
