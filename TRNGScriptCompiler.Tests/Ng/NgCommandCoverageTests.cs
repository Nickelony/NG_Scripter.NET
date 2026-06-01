using System.Globalization;
using System.Text;
using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
public sealed class NgCommandCoverageTests
{
	private const string ImportFileLine = "ImportFile= 9,extras\\matrix009.bin,FTYPE_USERFILE,IMPORT_TEMPORARY";

	// Each NG sample carries the words VB6 writes for it into its section. A command writes its header, the code times 256 plus the count of
	// the words that follow (Module1.bas:7154-7177, 7369), then each WORD, SLOT and STRING value as a word (:7580) and each LONG as its low
	// word, then its high word (:7551-7576). A slot given as a number is that number (:7432-7436), a string is its line in the language file,
	// the first at 0 (:6590-6596), and IGNORE is -1 (scripter_constants.txt).
	private static readonly NgSample[] s_ngCoverageOptionSamples =
	[
		// A Bool command writes no word but sets its definition's bit in the options flags word (Module1.bas:4571-4604, 8379-8383).
		new("Diagnostic= ENABLED", [], Flags: 0x0001),
		new("CRS= ENABLED", [], Flags: 0x0008),
		new("ForceBumpMapping= DISABLED", [], Flags: 0x0020),
		new("ShowLaraInTitle= ENABLED", [], Flags: 0x0040),
		new("NewSoundEngine= DISABLED", [], Flags: 0x0080),
		new("WorldFarView= 20480", [Header(5, 1), 20480]),
		new("Settings= 0", [Header(16, 1), 0]),
		new("SoundSettings= 1,2,3", [Header(24, 3), 1, 2, 3]),
		new("LogItem= 1,2", [Header(31, 2), 1, 2]),
		new("DiagnosticType= 1,2", [Header(36, 2), 1, 2]),
		// 123456 is 0x0001E240.
		new("CryptPersonalKey= 123456", [Header(47, 2), 0xE240, 0x0001]),
		// PluginMatrix is ExtraNG string 100, which VB6 writes with bit 15 set (Module1.bas:6597-6606).
		new("Plugin= 1,PluginMatrix,0,101,102", [Header(43, 6), 1, 0x8000 | 100, 0, 0, 101, 102]),
		// ImportFile= writes no mini-tag (Module1.bas:7658) but a chunk of its own, which ImportFileSample_WritesItsImportChunk checks.
		new(ImportFileLine, []),
	];

	private static readonly NgSample[] s_ngCoverageTitleSamples =
	[
		new("DefaultWindowsFont= 0,1,2,3,4,5,6,7,8,9,10,11,12,13", [Header(41, 14), 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]),
	];

	private static readonly NgSample[] s_ngCoverageLevelSamples =
	[
		// A Bool command writes no word but sets its definition's bit in the level flags word (Module1.bas:4571-4604, 8528-8532).
		new("ForceVolumetricFX= ENABLED", [], Flags: 0x0004),
		new("CutScene= ENABLED", [], Flags: 0x0040),
		new("PreserveInventory= ENABLED", [], Flags: 0x0080),
		new("AssignSlot= 9,1234", [Header(1, 3), 9, 1234, 0]),
		new("Snow= 1", [Header(2, 1), 1]),
		new("LevelFarView= 20480", [Header(3, 1), 20480]),
		new("FogRange= 1024,16384", [Header(4, 2), 1024, 16384]),
		new("TextFormat= 1,2,3,4", [Header(6, 4), 1, 2, 3, 4]),
		new("Rain= 1", [Header(7, 1), 1]),
		new("Damage= 1,2,3,4,Damage String,5", [Header(8, 7), 1, 2, 3, 4, 0, 8, 5]),
		new("Enemy= 9,1,2,3,4,5,6", [Header(9, 7), 9, 1, 2, 3, 4, 5, 6]),
		new("Animation= 1,2,3,4,5,6,7,8,9", [Header(10, 9), 1, 2, 3, 4, 5, 6, 7, 8, 9]),
		new("MirrorEffect= 1,2,3,4", [Header(11, 4), 1, 2, 3, 4]),
		new("Elevator= 1,2,3,4,5,6,7,8,9", [Header(12, 9), 1, 2, 3, 4, 5, 6, 7, 8, 9]),
		new("KeyPad= 1,2,3,4", [Header(13, 4), 1, 2, 3, 4]),
		new("AddEffect= 1,2,3,4,5,6,7,8,9,10,11", [Header(14, 11), 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]),
		new("Detector= 1,2,3,4,5", [Header(15, 5), 1, 2, 3, 4, 5]),
		// The nybble count 2, then the nybbles 4 and 5 packed as 4 + 5 * 16, low byte first (Module1.bas:7255-7268, 7610-7615).
		new("TextureSequence= 1,2,3,4,5", [Header(17, 4), 1, 2, 3, 0x5402]),
		new("Equipment= 9,1", [Header(18, 2), 9, 1]),
		new("MultEnvCondition= 1,2,3,4,5,6", [Header(19, 6), 1, 2, 3, 4, 5, 6]),
		new("Customize= 1,2,3", [Header(20, 4), 1, 0, 2, 3]),
		// No $ literal holds a plugin ID, so TriggerGroup= takes its word form, code 46 with one word per value (Module1.bas:7302-7337).
		new("TriggerGroup= 1,$2000,406,1,4,5,6", [Header(46, 7), 1, 0x2000, 406, 1, 4, 5, 6]),
		new("GlobalTrigger= 1,IGNORE,0,0,IGNORE,1,IGNORE", [Header(22, 8), 1, -1, 0, 0, 0, -1, 1, -1]),
		new("Organizer= 1,2,3,4,5,6,7", [Header(23, 7), 1, 2, 3, 4, 5, 6, 7]),
		new("ItemGroup= 1,9,10,11", [Header(25, 4), 1, 9, 10, 11]),
		new("ColorRGB= 1,2,3,4", [Header(26, 4), 1, 2, 3, 4]),
		new("Parameters= 1,2,3,4", [Header(27, 5), 1, 0, 2, 3, 4]),
		new("Turbo= 1,2", [Header(28, 2), 1, 2]),
		new("WindowTitle= Debug Window", [Header(29, 1), 9]),
		new("TestPosition= 1,2,9,4,5,6,7,8,9,10,11,12,13,14,15", [Header(30, 15), 1, 2, 9, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]),
		new("WindowsFont= 1,Font String,2,3,4,5", [Header(32, 6), 1, 10, 2, 3, 4, 5]),
		new("Diary= 1,2,3,4,5,Diary String,6,7", [Header(33, 8), 1, 2, 3, 4, 5, 11, 6, 7]),
		new("Image= 1,4,0,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE", [Header(34, 9), 1, 4, 0, -1, -1, -1, -1, -1, -1]),
		new("SavegamePanel= 1,2,3,4,5,Savegame String,6,7,8", [Header(35, 9), 1, 2, 3, 4, 5, 12, 6, 7, 8]),
		new("Switch= 1,2,3,4,5", [Header(37, 5), 1, 2, 3, 4, 5]),
		new("CombineItems= 1,2,3", [Header(38, 3), 1, 2, 3]),
		new("StandBy= 1,2,3,4,Standby String,5,6,7,8,9,10,11", [Header(39, 12), 1, 2, 3, 4, 13, 5, 6, 7, 8, 9, 10, 11]),
		new("AnimationSlot= 9,1,2,3,4,5,6,7,8,9,10", [Header(40, 11), 9, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]),
		new("Demo= 1,2,Demo One,Demo Two,3,4,5", [Header(42, 7), 1, 2, 14, 15, 3, 4, 5]),
		new("LaraStartPos= 1,2", [Header(44, 2), 1, 2]),
		new("StaticMIP= 1,2,3,4,5", [Header(45, 5), 1, 2, 3, 4, 5]),
	];

	private static readonly string[] s_supportedNgCommands =
	[
		// The word form of TriggerGroup= is no script command; TriggerGroup= reaches it.
		.. LoadCommandDefinitions()
			.Where(definition => definition.Value.Code != NgCommandCode.TriggerGroupWord)
			.Select(definition => definition.Key)
			.OrderBy(command => command, StringComparer.OrdinalIgnoreCase),
	];

	private static IReadOnlyDictionary<string, NgCommandDefinition> LoadCommandDefinitions()
	{
		string constantsPath = Path.Combine(AppContext.BaseDirectory, CompilerFileNames.ScripterConstants);

		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));

		return NgCommandCatalog.Parse(ConstantsFile.Load(constantsPath, diagnostics).Lines, constantsPath, diagnostics).Definitions;
	}

	private static readonly Lazy<IReadOnlyDictionary<string, NgSample>> s_ngCoverageOptionSampleMap = new(() =>
		CreateNgSampleMap(s_ngCoverageOptionSamples)
	);

	private static readonly Lazy<IReadOnlyDictionary<string, NgSample>> s_ngCoverageTitleSampleMap = new(() =>
		CreateNgSampleMap(s_ngCoverageTitleSamples)
	);

	private static readonly Lazy<IReadOnlyDictionary<string, NgSample>> s_ngCoverageLevelSampleMap = new(() =>
		CreateNgSampleMap(s_ngCoverageLevelSamples)
	);

	[TestMethod]
	[TestCategory("Fast")]
	public void Samples_CoverEveryMatrixDrivenCommand()
	{
		string[] missingNg = [.. s_supportedNgCommands.Where(command => GetNgSample(command) is null)];
		Assert.IsEmpty(missingNg, "Missing NG samples: " + string.Join(", ", missingNg));
	}

	[TestMethod]
	[TestCategory("IO")]
	[DynamicData(
		nameof(GetNgScenarios),
		DynamicDataDisplayName = nameof(CoverageWorkspace.GetScenarioDisplayName),
		DynamicDataDisplayNameDeclaringType = typeof(CoverageWorkspace)
	)]
	public void NgCommand_CompilesToItsWords(string scenarioName, ScenarioSection section, string commandLine)
	{
		NgSample sample = GetNgSample(scenarioName) ?? throw new AssertFailedException($"No NG sample for '{scenarioName}'.");
		var dat = ScriptDatReader.Read(CoverageWorkspace.CompileScriptDat(scenarioName, section, commandLine));

		// The title is the first NG level section and the level the second (Module1.bas:5229-5236). Each section ends with its flags mini-tag,
		// code 200 for [Options] and 201 for a level (:433-434), and a zero word (:8392-8400, 8549-8556).
		ScriptDatReader.NgField field = section switch
		{
			ScenarioSection.Options => dat.GetNgField((ushort)NgTagType.ScriptOptions),
			ScenarioSection.Title => dat.NgFields.Where(candidate => candidate.Tag == (ushort)NgTagType.ScriptLevel).ElementAt(0),
			_ => dat.NgFields.Where(candidate => candidate.Tag == (ushort)NgTagType.ScriptLevel).ElementAt(1),
		};

		int flagsCode = section == ScenarioSection.Options ? 200 : 201;

		AssertWords(
			[.. sample.Words, Header(flagsCode, 1), sample.Flags, 0],
			dat.GetDataWords(field),
			$"Scenario '{scenarioName}' wrote the wrong words for '{commandLine}'."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImportFileSample_WritesItsImportChunk()
	{
		// VB6 writes the ID, the import mode (IMPORT_TEMPORARY is 2), the file type (FTYPE_USERFILE is 1) and the number before the extension,
		// then the name in an 80-character field, which the String * 80 of Tipo_ImportFile pads with spaces, the size as two words and the file
		// bytes two to a word, low byte first (Module1.bas:6943-6965, 8607-8633; Mod_NextGeneration.bas:30-38).
		var dat = ScriptDatReader.Read(CoverageWorkspace.CompileScriptDat("ImportFile=", ScenarioSection.Options, ImportFileLine));
		byte[] nameField = [.. Encoding.ASCII.GetBytes("extras\\matrix009.bin\0"), .. Enumerable.Repeat((byte)' ', 80 - 21)];

		AssertWords(
			[9, 2, 1, 9, .. PackBytes(nameField), CoverageWorkspace.ImportFileData.Length, 0, .. PackBytes(CoverageWorkspace.ImportFileData)],
			dat.GetDataWords(dat.GetNgField((ushort)NgTagType.ImportFile)),
			"The import chunk holds the wrong words."
		);
	}

	public static IEnumerable<object[]> GetNgScenarios()
	{
		foreach (string command in s_supportedNgCommands)
		{
			NgSample? sample = GetNgSample(command);

			if (sample is null)
				continue;

			yield return CreateScenario(command, GetNgSection(command), sample.Line);
		}
	}

	private static object[] CreateScenario(string scenarioName, ScenarioSection section, string commandLine) => [scenarioName, section, commandLine];

	private static NgSample? GetNgSample(string command)
	{
		if (s_ngCoverageOptionSampleMap.Value.TryGetValue(command, out NgSample? sample))
			return sample;

		if (s_ngCoverageTitleSampleMap.Value.TryGetValue(command, out sample))
			return sample;

		if (s_ngCoverageLevelSampleMap.Value.TryGetValue(command, out sample))
			return sample;

		return null;
	}

	private static ScenarioSection GetNgSection(string command)
	{
		if (s_ngCoverageOptionSampleMap.Value.ContainsKey(command))
			return ScenarioSection.Options;

		if (s_ngCoverageTitleSampleMap.Value.ContainsKey(command))
			return ScenarioSection.Title;

		return ScenarioSection.Level;
	}

	private static void AssertWords(int[] expected, short[] actual, string message) =>
		Assert.AreEqual(FormatWords([.. expected.Select(word => unchecked((short)word))]), FormatWords(actual), message);

	private static string FormatWords(short[] words) =>
		string.Join(" ", words.Select(word => unchecked((ushort)word).ToString("X4", CultureInfo.InvariantCulture)));

	/// <summary>
	/// Returns the first word of an NG command: its code in the high byte and the count of the words that follow
	/// in the low byte.
	/// </summary>
	private static int Header(int code, int wordCount) => (code * 256) + wordCount;

	private static IEnumerable<int> PackBytes(byte[] bytes) =>
		Enumerable.Range(0, bytes.Length / 2).Select(i => bytes[2 * i] | (bytes[(2 * i) + 1] << 8));

	private static IReadOnlyDictionary<string, NgSample> CreateNgSampleMap(IEnumerable<NgSample> samples) =>
		samples.ToDictionary(sample => CoverageWorkspace.ExtractCommandName(sample.Line), StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// An NG sample line, the words VB6 writes for it into its section, and the bits it sets in the section's
	/// flags word.
	/// </summary>
	/// <param name="Line">The script line.</param>
	/// <param name="Words">
	/// The words of the command, its header first; none for a command that writes no mini-tag.
	/// </param>
	/// <param name="Flags">The bits the line sets in the flags word of its section.</param>
	private sealed record NgSample(string Line, int[] Words, int Flags = 0);
}
