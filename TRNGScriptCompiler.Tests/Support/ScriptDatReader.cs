using System.Buffers.Binary;
using System.Text;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Ng;

namespace TRNGScriptCompiler.Tests.Support;

/// <summary>
/// Splits a compiled <c>Script.dat</c> into its classic regions and NG fields, so a test can assert bytes
/// at a known position.
/// </summary>
/// <remarks>
/// The reader follows the file layout, not the encoder, and unscrambles the first 64 bytes of an encrypted
/// script whose plain bytes do not parse. That step calls the production <c>LegacyCipher</c>, so the independent tables
/// of <c>LegacyCipherTests</c> and the VB6 goldens check it, not this reader.
/// </remarks>
internal sealed class ScriptDatReader
{
	/// <summary>The NG field tag of the security block, <c>NGTAG_CONTROLLO_OPTIONS</c> in VB6.</summary>
	public const ushort SecurityTag = 0x8016;

	private const int ExtensionsOffset = 16;
	private const int ExtensionTableLength = 20;
	private const int LevelOffsetsOffset = ExtensionsOffset + (2 * ExtensionTableLength);

	private const ushort NgMarker = 0x474E;
	private const int NgLeMarker = 0x454C474E;

	// The NG trailer is two zero words, the NGLE marker and the header size.
	private const int NgTrailerLength = 12;

	private ScriptDatReader(byte[] bytes, bool headerScrambled)
	{
		Bytes = bytes;
		HeaderScrambled = headerScrambled;
	}

	/// <summary>Gets the file bytes with the header scramble undone.</summary>
	public byte[] Bytes { get; }

	/// <summary>
	/// Gets a value indicating whether the first 64 bytes were scrambled, which an encrypted script does.
	/// </summary>
	public bool HeaderScrambled { get; }

	public int Flags { get; private set; }

	public int InputTimeOut { get; private set; }

	public int Security { get; private set; }

	public int SectionCount { get; private set; }

	public int LevelCount { get; private set; }

	public IReadOnlyList<string> LevelPaths { get; private set; } = [];

	/// <summary>
	/// Gets the extensions of the PSX table, in slot order: <c>Level=</c>, <c>FMV=</c> and <c>Cut=</c>.
	/// </summary>
	public IReadOnlyList<string> PsxExtensions { get; private set; } = [];

	/// <summary>Gets the extensions of the PC table, in slot order.</summary>
	public IReadOnlyList<string> PcExtensions { get; private set; } = [];

	/// <summary>Gets the location of each classic section's bytes, in file order.</summary>
	public IReadOnlyList<Region> Sections { get; private set; } = [];

	/// <summary>Gets the language <c>.DAT</c> names the classic part lists.</summary>
	public IReadOnlyList<string> LanguageFiles { get; private set; } = [];

	/// <summary>Gets the offset of the <c>NG</c> marker that starts the NG header.</summary>
	public int NgOffset { get; private set; }

	/// <summary>Gets the NG header fields in file order; a zero-word separator is not a field.</summary>
	public IReadOnlyList<NgField> NgFields { get; private set; } = [];

	/// <summary>Reads a compiled <c>Script.dat</c>.</summary>
	/// <param name="bytes">The file bytes.</param>
	/// <returns>The parsed regions.</returns>
	/// <exception cref="InvalidDataException">
	/// The bytes do not parse as a <c>Script.dat</c>, scrambled or not.
	/// </exception>
	public static ScriptDatReader Read(byte[] bytes)
	{
		var plain = new ScriptDatReader(bytes, headerScrambled: false);

		if (plain.TryParse())
			return plain;

		var unscrambled = new ScriptDatReader(Unscramble(bytes), headerScrambled: true);

		if (unscrambled.TryParse())
			return unscrambled;

		throw new InvalidDataException("The bytes do not parse as a Script.dat, with or without the header scramble undone.");
	}

	/// <summary>Reads a compiled <c>Script.dat</c> from disk.</summary>
	public static ScriptDatReader ReadFile(string path) => Read(File.ReadAllBytes(path));

	/// <summary>Gets the concatenated bytes of every classic section.</summary>
	public ReadOnlySpan<byte> GetAllSections() =>
		Sections.Count == 0 ? [] : Bytes.AsSpan(Sections[0].Offset, Sections.Sum(section => section.Length));

	/// <summary>Gets the bytes of one classic section.</summary>
	/// <param name="index">The 0-based place of the section in the file.</param>
	/// <returns>The bytes of the section.</returns>
	public byte[] GetSectionBytes(int index) => Bytes.AsSpan(Sections[index].Offset, Sections[index].Length).ToArray();

	/// <summary>Gets the first NG field with a tag.</summary>
	public NgField GetNgField(ushort tag) => NgFields.First(field => field.Tag == tag);

	/// <summary>Gets the data words of an NG field, the word after its tag first.</summary>
	public short[] GetDataWords(NgField field)
	{
		short[] words = new short[field.DataWordCount];

		for (int i = 0; i < words.Length; i++)
			words[i] = BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(field.DataOffset + (2 * i)));

		return words;
	}

	/// <summary>Gets the mini-tags of a script-options or level field, in file order.</summary>
	/// <param name="field">An NG field whose data words are mini-tags, ended by a zero word.</param>
	/// <returns>The mini-tags, without the ending zero word.</returns>
	/// <exception cref="InvalidDataException">A mini-tag counts more words than the field holds.</exception>
	public IReadOnlyList<MiniTag> GetMiniTags(NgField field)
	{
		short[] words = GetDataWords(field);
		var tags = new List<MiniTag>();

		for (int start = 0; start < words.Length && words[start] != 0; )
		{
			int count = words[start] & 0xFF;

			if (start + count >= words.Length)
				throw new InvalidDataException($"The mini-tag at data word {start} counts {count} words, past the end of the field.");

			tags.Add(new MiniTag(Code: (words[start] >> 8) & 0xFF, Words: words[(start + 1)..(start + 1 + count)]));
			start += count + 1;
		}

		return tags;
	}

	/// <summary>Gets the bytes of the file that an <c>ImportFile</c> chunk holds.</summary>
	/// <param name="field">The NG field of the chunk.</param>
	/// <returns>
	/// The payload, as long as the size the chunk declares: it follows the import ID, mode, file type and number,
	/// the 80-character name and the size as two words.
	/// </returns>
	public byte[] GetImportedBytes(NgField field)
	{
		const int payloadWords = 4 + 40 + 2;

		short[] words = GetDataWords(field);
		int size = (ushort)words[payloadWords - 2] | ((ushort)words[payloadWords - 1] << 16);

		return Bytes.AsSpan(field.DataOffset + (2 * payloadWords), size).ToArray();
	}

	/// <summary>Names the region that holds a byte offset, for a failure message.</summary>
	/// <param name="offset">A byte offset into the file.</param>
	/// <returns>
	/// A description such as "classic section 2, byte 14" or "NG field 0x800C (index 3), mini-tag 0x2A, word 1".
	/// </returns>
	public string Describe(int offset)
	{
		if (offset < ExtensionsOffset)
			return $"classic header, byte {offset}{(HeaderScrambled && offset < LegacyCipher.ScriptDatHeaderLength ? " (scrambled)" : string.Empty)}";

		if (offset < LevelOffsetsOffset)
			return offset < ExtensionsOffset + ExtensionTableLength ? "PSX extension table" : "PC extension table";

		for (int i = 0; i < Sections.Count; i++)
		{
			if (offset >= Sections[i].Offset && offset < Sections[i].Offset + Sections[i].Length)
				return $"classic section {i}, byte {offset - Sections[i].Offset}";
		}

		if (offset < NgOffset)
			return Sections.Count > 0 && offset < Sections[0].Offset ? "classic level path or offset tables" : "classic language file names";

		if (offset < NgOffset + 2)
			return "NG marker";

		for (int i = 0; i < NgFields.Count; i++)
		{
			NgField field = NgFields[i];

			if (offset >= field.Offset && offset < field.DataOffset + (2 * field.DataWordCount))
				return DescribeInField(field, i, offset);
		}

		return offset >= Bytes.Length - NgTrailerLength ? "NG trailer" : "NG zero-word separator";
	}

	private static byte[] Unscramble(byte[] bytes)
	{
		// The scramble is a fixed permutation plus a repeating XOR, so scrambling an identity probe and a zero probe recovers both.
		Span<byte> identity = stackalloc byte[LegacyCipher.ScriptDatHeaderLength];
		Span<byte> zero = stackalloc byte[LegacyCipher.ScriptDatHeaderLength];

		for (int i = 0; i < identity.Length; i++)
			identity[i] = (byte)i;

		LegacyCipher.TryScrambleScriptDatHeader(identity);
		LegacyCipher.TryScrambleScriptDatHeader(zero);

		byte[] plain = (byte[])bytes.Clone();

		for (int i = 0; i < LegacyCipher.ScriptDatHeaderLength && i < bytes.Length; i++)
			plain[identity[i] ^ zero[i]] = (byte)(bytes[i] ^ zero[i]);

		return plain;
	}

	private string DescribeInField(NgField field, int fieldIndex, int offset)
	{
		string name = $"NG field 0x{field.Tag:X4} (index {fieldIndex})";

		if (offset < field.DataOffset)
			return $"{name}, size or tag word";

		int word = (offset - field.DataOffset) / 2;

		if (field.Tag is not ((ushort)NgTagType.ScriptOptions or (ushort)NgTagType.ScriptLevel))
			return $"{name}, data word {word}";

		int miniTagStart = 0;

		while (miniTagStart < field.DataWordCount)
		{
			ushort header = ReadWord(field.DataOffset + (2 * miniTagStart));
			int miniTagWords = header & 0xFF;

			if (miniTagWords == 0 || word <= miniTagStart + miniTagWords)
				return $"{name}, mini-tag 0x{header >> 8:X2}, word {word - miniTagStart}";

			miniTagStart += miniTagWords + 1;
		}

		return $"{name}, data word {word}";
	}

	private bool TryParse()
	{
		if (Bytes.Length < LevelOffsetsOffset + NgTrailerLength || !TryLocateNgHeader())
			return false;

		Flags = BinaryPrimitives.ReadInt32LittleEndian(Bytes);
		InputTimeOut = BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(4));
		Security = Bytes[8];
		SectionCount = Bytes[9];
		LevelCount = BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(10));

		int levelNamesSize = BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(12));
		int sectionsSize = BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(14));

		if (LevelCount < 0 || levelNamesSize < 0 || sectionsSize < 0)
			return false;

		int levelNamesStart = LevelOffsetsOffset + (2 * LevelCount);
		int sectionOffsetsStart = levelNamesStart + levelNamesSize;
		int sectionsStart = sectionOffsetsStart + (2 * SectionCount);
		int languageNamesStart = sectionsStart + sectionsSize;

		if (languageNamesStart > NgOffset)
			return false;

		var levelPaths = new List<string>();

		for (int i = 0; i < LevelCount; i++)
		{
			int start = levelNamesStart + BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(LevelOffsetsOffset + (2 * i)));

			if (!TryReadNullTerminated(start, limit: sectionOffsetsStart, text: out string path, next: out _))
				return false;

			levelPaths.Add(path);
		}

		var sections = new List<Region>();

		for (int i = 0; i < SectionCount; i++)
		{
			int start = BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(sectionOffsetsStart + (2 * i)));
			int end = i + 1 < SectionCount ? BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(sectionOffsetsStart + (2 * (i + 1)))) : sectionsSize;

			if (start < 0 || end < start || end > sectionsSize)
				return false;

			sections.Add(new Region(sectionsStart + start, end - start));
		}

		var languageFiles = new List<string>();

		for (int position = languageNamesStart; position < NgOffset; )
		{
			if (
				!TryReadNullTerminated(start: position, limit: NgOffset, text: out string name, next: out position)
				|| !name.EndsWith(".DAT", StringComparison.Ordinal)
			)
			{
				return false;
			}

			languageFiles.Add(name);
		}

		PsxExtensions = ReadExtensionTable(ExtensionsOffset);
		PcExtensions = ReadExtensionTable(ExtensionsOffset + ExtensionTableLength);
		LevelPaths = levelPaths;
		Sections = sections;
		LanguageFiles = languageFiles;
		return TryParseNgFields();
	}

	// A table holds the extension of each slot below the count, each ended by a zero byte, and zero bytes to fill its field.
	private List<string> ReadExtensionTable(int offset)
	{
		List<string> extensions = [.. Vb6Text.GetString(Bytes, offset, ExtensionTableLength).Split('\0')];

		while (extensions.Count > 0 && extensions[^1].Length == 0)
			extensions.RemoveAt(extensions.Count - 1);

		return extensions;
	}

	private bool TryLocateNgHeader()
	{
		int trailer = Bytes.Length - 8;

		if (BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(trailer)) != NgLeMarker)
			return false;

		NgOffset = Bytes.Length - BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(trailer + 4));
		return NgOffset >= LevelOffsetsOffset && ReadWord(NgOffset) == NgMarker;
	}

	private bool TryParseNgFields()
	{
		var fields = new List<NgField>();

		int end = Bytes.Length - NgTrailerLength;
		int position = NgOffset + 2;

		while (position < end)
		{
			ushort first = ReadWord(position);

			if (first == 0)
			{
				position += 2;
				continue;
			}

			// A long-form size sets the top bit and spreads the word count over two words.
			bool isLong = (first & 0x8000) != 0;

			int totalWords = isLong ? ((first & 0x7FFF) << 16) | ReadWord(position + 2) : first;
			int headerWords = isLong ? 3 : 2;

			if (totalWords < headerWords || position + (2 * totalWords) > end)
				return false;

			int tagOffset = position + (2 * (headerWords - 1));
			fields.Add(new NgField(Offset: position, Tag: ReadWord(tagOffset), DataOffset: tagOffset + 2, DataWordCount: totalWords - headerWords));
			position += 2 * totalWords;
		}

		NgFields = fields;
		return position == end;
	}

	private bool TryReadNullTerminated(int start, int limit, out string text, out int next)
	{
		int terminator = start >= 0 && start < limit ? Array.IndexOf(array: Bytes, value: (byte)0, startIndex: start, count: limit - start) : -1;

		text = terminator < 0 ? string.Empty : Vb6Text.GetString(Bytes, start, terminator - start);
		next = terminator + 1;
		return terminator >= 0;
	}

	private ushort ReadWord(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(offset));

	private static Encoding Vb6Text => Vb6Ansi.Encoding;

	/// <summary>A run of bytes in the file.</summary>
	/// <param name="Offset">The offset of the first byte.</param>
	/// <param name="Length">The number of bytes.</param>
	internal readonly record struct Region(int Offset, int Length);

	/// <summary>One NG header field: its size words, its tag, and its data words.</summary>
	/// <remarks>
	/// <c>Offset</c> locates the first size word and <c>DataOffset</c> the first data word; a level section's
	/// tag is <c>0x800C</c>, for example.
	/// </remarks>
	internal readonly record struct NgField(int Offset, ushort Tag, int DataOffset, int DataWordCount);

	/// <summary>One mini-tag of a level or options field: a header word and the words it counts.</summary>
	/// <param name="Code">The command code, the high byte of the header word.</param>
	/// <param name="Words">The words after the header; the low byte of the header counts them.</param>
	internal readonly record struct MiniTag(int Code, short[] Words)
	{
		/// <summary>Gets the header word and the words, as the file holds them.</summary>
		/// <returns>The bytes of the mini-tag, low byte first.</returns>
		public byte[] ToBytes()
		{
			short[] all = [(short)((Code << 8) | Words.Length), .. Words];
			return [.. all.SelectMany(word => new[] { (byte)word, (byte)(word >> 8) })];
		}
	}
}
