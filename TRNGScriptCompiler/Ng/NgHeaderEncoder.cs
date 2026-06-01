using System.Runtime.InteropServices;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Numerics;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Encodes the NG payload appended to <c>Script.dat</c>: the <c>[Options]</c> section, the security header,
/// every level section, the import-file section and, for an encrypted script, the level-names chunk.
/// </summary>
internal sealed class NgHeaderEncoder
{
	private readonly CompilerLog _logger;
	private readonly DiagnosticSink _diagnostics;
	private readonly NgImportChunkEncoder _importEncoder;
	private readonly double? _deterministicSeed;

	/// <summary>The clock of an unseeded security block, a test seam.</summary>
	private readonly Func<double>? _timerSeconds;

	/// <remarks>
	/// A <see langword="null"/> seed seeds the random padding of the security block from the timer, as VB6 does.
	/// </remarks>
	internal NgHeaderEncoder(CompilerLog logger, DiagnosticSink diagnostics, double? deterministicSeed, Func<double>? timerSeconds = null)
	{
		_logger = logger;
		_diagnostics = diagnostics;
		_deterministicSeed = deterministicSeed;
		_timerSeconds = timerSeconds;
		_importEncoder = new NgImportChunkEncoder(logger, diagnostics);
	}

	/// <summary>
	/// Encodes the NG payload of a parsed script, failing when the options or a level section holds more words
	/// than its count holds, when an import fails, or unless the emitted mini-tag structure matches the expected count.
	/// </summary>
	/// <returns>The encoded payload, or <see langword="null"/> after an error that the sink holds.</returns>
	/// <remarks>
	/// The result defers the encryption that <c>Image=</c> commands request to publication, so an analysis
	/// encrypts nothing.
	/// </remarks>
	internal NgHeaderEncodingResult? Encode(ScriptModel scriptModel)
	{
		_logger.LogVerbose("Saving Header NG Data...");

		NgScriptModel ngModel = scriptModel.NgModel;

		var allWords = new List<short>();
		var imageEncryptRequests = new List<NgImageReference>();

		int totalMiniTagsExpected = 0;
		int optionsCountIndex = allWords.ReserveCount();

		WriteOptionsSection(allWords, ngModel);
		totalMiniTagsExpected += ngModel.OptionsCommands.Commands.Count + 1;

		// VB6 stores the count in an Integer, which overflows past 32767 (Module1.bas:8407).
		int optionsWordCount = allWords.CountFrom(optionsCountIndex);

		if (optionsWordCount > short.MaxValue)
		{
			_diagnostics.Error(
				$"The NG options section holds {optionsWordCount} words; the format holds at most {short.MaxValue}",
				scriptModel.Parsed.OptionsHeaderLocation
			);

			return null;
		}

		allWords.PatchCount(optionsCountIndex);

		// Each encoding draws from a generator of its own, so a seeded run writes the same padding every time.
		NgSecurityBlockEncoder.Encode(allWords, scriptModel, new Vb6Random(_deterministicSeed, _timerSeconds));

		if (!WriteAllLevelSections(allWords, ngModel, sections: scriptModel.Parsed.Sections, imageEncryptRequests, ref totalMiniTagsExpected))
			return null;

		if (!_importEncoder.TryEncodeAll(allWords, ngModel.ImportFiles))
			return null;

		if (ngModel.EnableScriptEncryption)
			NgLevelNamesEncoder.Encode(allWords, scriptModel);

		if (!NgHeaderVerifier.Verify(allWords, totalMiniTagsExpected, _diagnostics))
			return null;

		_logger.LogSeparator();

		return new NgHeaderEncodingResult
		{
			HeaderBytes = NgWords.BuildHeaderBytes(CollectionsMarshal.AsSpan(allWords)),
			ImageEncryptRequests = imageEncryptRequests,
			PlainImages = CollectPlainImages(ngModel),
		};
	}

	private void WriteOptionsSection(List<short> allWords, NgScriptModel ngModel)
	{
		_logger.LogVerbose("[OPTIONS] Section ...");

		var optionsWords = new List<short>();
		allWords.Add(unchecked((short)NgTagType.ScriptOptions));

		foreach (NgCommand command in ngModel.OptionsCommands.Commands)
		{
			_logger.LogVerbose($"Scanning: {command.CommandName}", indent: 1);
			optionsWords.AddRange(command.CompiledWords);
		}

		const int flagsHeader = ((int)NgCommandCode.FlagsOption * 256) + 1;

		optionsWords.Add(unchecked((short)flagsHeader));
		optionsWords.Add((short)ngModel.OptionsCommands.OptionFlags);
		optionsWords.Add(0);

		allWords.AddRange(optionsWords);
	}

	private bool WriteLevelSection(
		List<short> allWords,
		NgCommandGroup levelCommands,
		int levelIndex,
		SourceLocation headerLocation,
		List<NgImageReference> imageEncryptRequests
	)
	{
		int countIndex = allWords.ReserveCount();
		allWords.Add(unchecked((short)NgTagType.ScriptLevel));

		foreach (NgCommand command in levelCommands.Commands)
		{
			_logger.LogVerbose($"Scanning: {command.CommandName}", indent: 1);

			if (TryCreateEncryptImageRequest(command, out NgImageReference? request))
				imageEncryptRequests.Add(request);

			allWords.AddRange(command.CompiledWords);
		}

		const int flagsHeader = ((int)NgCommandCode.FlagsLevel * 256) + 1;

		allWords.Add(unchecked((short)flagsHeader));
		allWords.Add((short)levelCommands.LevelFlags);
		allWords.Add(0);

		// The written count includes the size and tag words, and VB6 checks that count (Module1.bas:8559-8565).
		int sectionWordCount = allWords.CountFrom(countIndex);

		if (sectionWordCount > short.MaxValue)
		{
			_diagnostics.Error(
				$"NG level section {levelIndex} holds {sectionWordCount} words; the format holds at most {short.MaxValue}",
				headerLocation
			);

			return false;
		}

		allWords.PatchCount(countIndex);

		_logger.LogVerbose($"====== TOT WORD PER SCRIPT_LEVEL SECTION = {sectionWordCount - 2}");
		return true;
	}

	private bool WriteAllLevelSections(
		List<short> allWords,
		NgScriptModel ngModel,
		IReadOnlyList<ParsedSection> sections,
		List<NgImageReference> imageEncryptRequests,
		ref int totalMiniTagsExpected
	)
	{
		for (int i = 0; i < ngModel.LevelCommands.Count; i++)
		{
			_logger.LogSeparator();

			_logger.LogVerbose("========== START LEVEL PARSING ==========");
			_logger.LogVerbose($"[LEVEL] : Level {i}");

			if (
				!WriteLevelSection(
					allWords,
					levelCommands: ngModel.LevelCommands[i],
					levelIndex: i,
					headerLocation: sections[i].HeaderLocation,
					imageEncryptRequests
				)
			)
			{
				return false;
			}

			totalMiniTagsExpected += ngModel.LevelCommands[i].Commands.Count + 1;

			_logger.LogVerbose("========= END LEVEL PARSING =========");
		}

		return true;
	}

	private static bool TryCreateEncryptImageRequest(NgCommand command, out NgImageReference request)
	{
		request = new NgImageReference(ImageId: 0, Location: default);

		if (!TryReadImageCommand(command, out int imageId, out bool isCryptic) || !isCryptic)
			return false;

		request = new NgImageReference(imageId, command.Location);

		return true;
	}

	private static List<NgImageReference> CollectPlainImages(NgScriptModel ngModel)
	{
		var plainImages = new List<NgImageReference>();

		foreach (NgCommandGroup levelCommands in ngModel.LevelCommands)
		{
			foreach (NgCommand command in levelCommands.Commands)
			{
				if (TryReadImageCommand(command, out int imageId, out bool isCryptic) && !isCryptic)
					plainImages.Add(new NgImageReference(imageId, command.Location));
			}
		}

		return plainImages;
	}

	private static bool TryReadImageCommand(NgCommand command, out int imageId, out bool isCryptic)
	{
		imageId = 0;
		isCryptic = false;

		if (command.CommandCode != NgCommandCode.Image || command.CompiledWords.Count < 4)
			return false;

		short rawFlags = command.CompiledWords[3];
		imageId = unchecked((ushort)command.CompiledWords[2]);

		// VB6 skips encryption when the flags argument is absent (-1) or the IF_CRYPTIC
		// bit (&H4000) is clear (Module1.bas:8505-8511).
		isCryptic = rawFlags != -1 && (rawFlags & 0x4000) != 0;

		return true;
	}
}
