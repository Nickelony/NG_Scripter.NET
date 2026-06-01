using System.Buffers.Binary;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
[TestCategory("Fast")]
public sealed class NgHeaderEncoderTests
{
	private const double Seed = TestWorkspace.DefaultDeterministicSeed;

	private static readonly short s_optionsFlags = unchecked((short)((200 * 256) + 1));
	private static readonly short s_levelFlags = unchecked((short)((201 * 256) + 1));

	private static NgCommand Command(NgCommandCode code, int line, params short[] words)
	{
		return new()
		{
			CommandName = code.ToString() + "=",
			CommandCode = code,
			Arguments = [],
			CompiledWords = words,
			Location = new SourceLocation(line, File: "Script.txt"),
		};
	}

	private static ScriptModel Script(UnitRun unit, NgScriptModel ngModel)
	{
		var parsed = new ParsedScript
		{
			Sections =
			[
				new ParsedSection
				{
					SectionType = ScriptSectionType.Level,
					LevelPath = @"data\test",
					HeaderLocation = unit.At(4),
				},
			],
		};

		return new ScriptModel(parsed, ngModel);
	}

	private static (NgHeaderEncodingResult? Result, UnitRun Unit) Encode(Func<UnitRun, NgScriptModel> ngModel)
	{
		var unit = new UnitRun();
		NgHeaderEncodingResult? result = new NgHeaderEncoder(unit.Run.Logger, unit.Run.Diagnostics, Seed).Encode(Script(unit, ngModel(unit)));
		return (result, unit);
	}

	/// <summary>
	/// Reads the words between the NG marker and the trailer: two zero words, NGLE and the header size.
	/// </summary>
	private static List<short> ChunkWords(byte[] header)
	{
		Assert.AreEqual(0x474E, BinaryPrimitives.ReadInt16LittleEndian(header), "The header starts with the NG marker.");
		Assert.AreEqual(0x454C474E, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(header.Length - 8)), "The header ends with NGLE.");

		Assert.AreEqual(
			header.Length,
			BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(header.Length - 4)),
			"The header ends with its own size."
		);

		return
		[
			.. Enumerable.Range(0, (header.Length - 14) / 2).Select(index => BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(2 + (2 * index)))),
		];
	}

	[TestMethod]
	public void Header_WritesTheOptionsTheSecurityBlockAndEachLevelSection()
	{
		// VB6 writes the [Options] section with its commands, its flags mini-tag and a zero word, the security block, then each level section
		// alike, each section headed by its word count and its tag (Module1.bas:8293-8415, 8432-8576).
		(NgHeaderEncodingResult? result, UnitRun unit) = Encode(_ => new NgScriptModel
		{
			OptionsCommands = new NgCommandGroup
			{
				Commands = [Command(NgCommandCode.Settings, 2, (16 * 256) + 1, 0)],
				OptionFlags = (NgMainFlags)0x0021,
			},

			LevelCommands = [new NgCommandGroup { Commands = [Command(NgCommandCode.Snow, 5, (2 * 256) + 1, 1)], LevelFlags = (NgLevelFlags)0x0048 }],
		});

		Assert.IsNotNull(result);

		List<short> words = ChunkWords(result.HeaderBytes);
		int securityWords = words[7];

		short[] options = [7, unchecked((short)NgTagType.ScriptOptions), (16 * 256) + 1, 0, s_optionsFlags, 0x0021, 0];
		short[] level = [7, unchecked((short)NgTagType.ScriptLevel), (2 * 256) + 1, 1, s_levelFlags, 0x0048, 0];

		Assert.AreSequenceEqual(options, words.Take(7));
		Assert.AreEqual(unchecked((short)NgTagType.Security), words[8]);
		Assert.AreSequenceEqual(level, words.Skip(7 + securityWords));

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	public void EncryptedScript_EndsWithTheLevelNames()
	{
		// VB6 adds the level names chunk after the imports when Settings= sets SET_CRYPT_SCRIPT (Module1.bas:8654-8695).
		(NgHeaderEncodingResult? result, _) = Encode(_ => new NgScriptModel
		{
			LevelCommands = [new NgCommandGroup()],
			EnableScriptEncryption = true,
			NgSettings = 8,
		});

		Assert.IsNotNull(result);

		List<short> words = ChunkWords(result.HeaderBytes);
		short[] levelNames = [(10 / 2) + 3, unchecked((short)NgTagType.LevelNames), 10, 0x6164, 0x6174, 0x745C, 0x7365, 0x2074];

		Assert.AreSequenceEqual(levelNames, words.TakeLast(levelNames.Length));
	}

	[TestMethod]
	public void ImageWithIfCryptic_AsksForItsBitmapToBeEncrypted()
	{
		// VB6 encrypts the bitmap of an Image= whose flags are not -1 and hold IF_CRYPTIC, $4000 (Module1.bas:8504-8512); the port defers the
		// encryption to publication and lists the other images.
		static short[] Image(short file, short flags) => [(34 * 256) + 9, 1, file, flags, 0, 0, 0, 0, 0, 0];

		(NgHeaderEncodingResult? result, UnitRun unit) = Encode(_ => new NgScriptModel
		{
			LevelCommands =
			[
				new NgCommandGroup
				{
					Commands =
					[
						Command(NgCommandCode.Image, 5, Image(5, 0x4000)),
						Command(NgCommandCode.Image, 6, Image(6, 0)),
						Command(NgCommandCode.Image, 7, Image(7, -1)),
					],
				},
			],
		});

		Assert.IsNotNull(result);
		Assert.AreEqual((5, 5), (result.ImageEncryptRequests.Single().ImageId, result.ImageEncryptRequests.Single().Location.Line));

		Assert.AreSequenceEqual([(6, 6), (7, 7)], result.PlainImages.Select(image => (image.ImageId, image.Location.Line)));
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	public void LevelSectionPastTheWord_Fails()
	{
		// VB6 stops on a level section of more than 32767 words, counted with its size and tag words (Module1.bas:8561-8574).
		short[] largest = [(10 * 256) + 255, .. new short[255]];

		(NgHeaderEncodingResult? result, UnitRun unit) = Encode(_ => new NgScriptModel
		{
			LevelCommands =
			[
				new NgCommandGroup { Commands = [.. Enumerable.Range(0, 130).Select(index => Command(NgCommandCode.Animation, 5 + index, largest))] },
			],
		});

		Assert.IsNull(result);
		DiagnosticAssert.SingleError(unit.Diagnostics, 4, "NG level section 0 holds 33285 words; the format holds at most 32767");
	}

	[TestMethod]
	public void CommandWhoseWordsDisagreeWithItsCount_FailsTheVerification()
	{
		// VB6 verifies the header it wrote before it saves it (Module1.bas:8697-8701).
		(NgHeaderEncodingResult? result, UnitRun unit) = Encode(_ => new NgScriptModel
		{
			LevelCommands = [new NgCommandGroup { Commands = [Command(NgCommandCode.Snow, 5, (2 * 256) + 5, 1)] }],
		});

		Assert.IsNull(result);

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			line: null,
			"The NG header is corrupt, because the mini-tag 0x02 runs past the end of its section"
		);
	}
}
