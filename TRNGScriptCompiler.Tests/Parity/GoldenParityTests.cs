using System.Buffers.Binary;
using System.Security.Cryptography;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Parity;

/// <summary>
/// Compares the parity fixture with the seeded outputs the instrumented VB6 build wrote, whose seeds the testing
/// guide lists.
/// </summary>
[TestClass]
[TestCategory("Parity")]
public sealed class GoldenParityTests(TestContext testContext)
{
	// The SHA-256 of each file the goldens were compiled from, keyed by its path below Fixtures/Workspace, which holds no other file but
	// the notes.
	private static readonly Dictionary<string, string> s_inputHashes = new(StringComparer.Ordinal)
	{
		["Objects.h"] = "ceb78cd541c54c0eeff19148aa6c3fab72af1499d32b5f2a6cc9fa909386ced0",
		["PluginDemo.script"] = "ccdfc9b4c3e650a7b3ee963707d8d0a644ce309c5bf6292476532ff2919ec2df",
		["extras/demo007.bin"] = "4555f7fe65298219a7c29f37c6bcae7d0ce2bb14618a002d5b49040562e72e79",
		["pix/@image4.bmp"] = "c144ee45d8c0726793ee2f670a490b49ca227c4c74eb397f4478a276703751b1",
		["pix/image1.bmp"] = "6db98e45c05b64b18cbb4fa1b2c6d3ed8b73612019313fedfc8bc8dd59db5c76",
		["script/English.txt"] = "76c0f641fce44e69b995516c6c780cf7f71fe08a33603fb7d1818575df7f21e4",
		["script/SCRIPT.TXT"] = "38647ec41372d215d5b6d66f78c64a0e5c0ab2f089be17fcc104760f33e7a56c",
		["script/exotic_commands.inc"] = "7304c183131ced3259745b9fb614c9320bb2d76e4efa1647ed3eeab39fc28720",
		["script/intro.txt"] = "5e827d42965e9267b85e3bd8646da813041531789d5bae7c535fb4ea54fbdc91",
		["script/ng_variants.inc"] = "c530936ecb305756d6ee13d82bc25c55e5d2f115516776fd14780d62ba54c359",
	};

	// The notes below Fixtures/Workspace, which the compiler does not read.
	private static readonly string[] s_notes = ["README.md"];

	// The SHA-256 of the shipped scripter_constants.txt with the CRLF line ends .gitattributes checks out, which the compile reads from the
	// build output, where TRNGCompilerOptions.CreateDefaults names it.
	private const string ConstantsHash = "ad54cb3107a9a2614fefa5ac7d2384abff6e663fe4edaa56b7467d7706924b87";

	[TestMethod]
	[DataRow(12345)]
	[DataRow(54321)]
	[DataRow(99999)]
	public void SeededCompile_MatchesTheVb6Outputs(int seed)
	{
		using TestWorkspace workspace = CopyFixture();

		TestCompileResult result = workspace.Compile(workspace.CreateOptions(mainLanguage: "English", deterministicSeed: seed));

		Assert.IsTrue(result.Success, result.Log);

		GoldenAssert.FileMatches(ReadGolden(seed, "Script.dat"), workspace.PathOf("Script.dat"), testContext);
		GoldenAssert.FileMatches(ReadGolden(seed, "English.dat"), workspace.PathOf("English.dat"), testContext);
	}

	[TestMethod]
	[DataRow(12345)]
	[DataRow(54321)]
	[DataRow(99999)]
	public void UnseededCompile_MatchesTheVb6OutputsOutsideTheSecurityBlock(int seed)
	{
		// A release compile seeds the security block's padding from the timer, as pristine VB6 does (PD-07), so only that block
		// differs from a golden, whichever seed the golden came from.
		using TestWorkspace workspace = CopyFixture();

		TestCompileResult result = workspace.Compile(TRNGCompilerOptions.CreateDefaults(workspace.RootPath, "English"));

		Assert.IsTrue(result.Success, result.Log);

		byte[] scriptDat = workspace.ReadScriptDat();
		byte[] golden = ReadGolden(seed, "Script.dat");

		string strippedPath = Path.Combine("without-security-block", "Script.dat");
		workspace.WithBinaryFile(strippedPath, WithoutSecurityBlock(scriptDat));

		AssertSecurityBlockIsWellFormed(ScriptDatReader.Read(scriptDat), ScriptDatReader.Read(golden));

		GoldenAssert.FileMatches(WithoutSecurityBlock(golden), workspace.PathOf(strippedPath), testContext);
		GoldenAssert.FileMatches(ReadGolden(seed, "English.dat"), workspace.PathOf("English.dat"), testContext);
	}

	// Checks the hand-derived shape of AggiungiHeaderSecurity (Module1.bas:7960-8045) in a block whose padding is random.
	private static void AssertSecurityBlockIsWellFormed(ScriptDatReader actual, ScriptDatReader golden)
	{
		int index = SecurityBlockIndex(actual);

		Assert.AreEqual(SecurityBlockIndex(golden), index, "The security block sits at the place VB6 writes it, after the [Options] block.");

		ScriptDatReader.NgField block = actual.NgFields[index];

		// TotDati = Casuale(9) + 20 data words (Module1.bas:7984), behind a size word of TotDati + 2 and the tag (:8030-8034).
		Assert.IsInRange(
			minValue: 20,
			maxValue: 28,
			value: block.DataWordCount,
			message: $"The security block holds {block.DataWordCount} data words, but VB6 writes 20 to 28."
		);

		Assert.AreEqual(block.DataWordCount + 2, BinaryPrimitives.ReadUInt16LittleEndian(actual.Bytes.AsSpan(block.Offset)));

		byte[] plain = Decode(actual, block);
		byte[] goldenPlain = Decode(golden, golden.NgFields[index]);

		// Bytes 5, 12 and 19 copy the level count, the options flags and the NG settings (Module1.bas:8004-8008).
		Assert.AreEqual((byte)actual.LevelCount, plain[5], "Byte 5 is the level count.");
		Assert.AreEqual((byte)actual.Flags, plain[12], "Byte 12 is the options flags.");
		Assert.AreEqual(goldenPlain[19], plain[19], "Byte 19 is the NG settings.");

		// Byte 0 is the low byte of the sum of the others before the XOR (Module1.bas:8011-8018).
		Assert.AreEqual((byte)plain.Skip(1).Sum(value => value), plain[0], "Byte 0 is the checksum.");

		// Each other even byte is 4 or 6, and each other odd byte is below 16 (Module1.bas:7989-8001).
		for (int i = 1; i < plain.Length; i++)
		{
			if (i is 5 or 12 or 19)
				continue;

			bool padding = i % 2 == 0 ? plain[i] is 4 or 6 : plain[i] < 16;
			Assert.IsTrue(padding, $"Padding byte {i} of the security block is {plain[i]}.");
		}
	}

	private static int SecurityBlockIndex(ScriptDatReader dat)
	{
		int[] indexes = [.. Enumerable.Range(0, dat.NgFields.Count).Where(i => dat.NgFields[i].Tag == ScriptDatReader.SecurityTag)];

		Assert.HasCount(1, indexes, "A Script.dat holds one security block.");

		return indexes[0];
	}

	private static byte[] Decode(ScriptDatReader dat, ScriptDatReader.NgField block) =>
		SecurityBlockDecoder.Decode(dat.Bytes.AsSpan(block.DataOffset, 2 * block.DataWordCount));

	// Removes the security block and takes its length off the NG header size that ends the file, so the rest compares byte for byte
	// whatever the padding length, and the result still parses as a Script.dat for a failure message.
	private static byte[] WithoutSecurityBlock(byte[] file)
	{
		var dat = ScriptDatReader.Read(file);

		ScriptDatReader.NgField block = dat.NgFields[SecurityBlockIndex(dat)];
		int end = block.DataOffset + (2 * block.DataWordCount);

		byte[] stripped = [.. file[..block.Offset], .. file[end..]];
		Span<byte> headerSize = stripped.AsSpan(stripped.Length - 4);

		BinaryPrimitives.WriteInt32LittleEndian(headerSize, BinaryPrimitives.ReadInt32LittleEndian(headerSize) - (end - block.Offset));

		return stripped;
	}

	// An input that changed, such as a text file whose line endings git converted, and an input added or removed, fail here by name,
	// before they can fail the comparison at an output offset.
	private static TestWorkspace CopyFixture()
	{
		string root = TestFixtures.WorkspaceDirectory;
		string constantsPath = Path.Combine(AppContext.BaseDirectory, CompilerFileNames.ScripterConstants);

		var actual = Directory
			.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
			.Where(key => !s_notes.Contains(key, StringComparer.Ordinal))
			.ToDictionary(key => key, key => HashOf(Path.Combine(root, key)), StringComparer.Ordinal);

		string[] changed =
		[
			.. s_inputHashes
				.Keys.Union(actual.Keys, StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
				.Select(key => DescribeChange($"Fixtures/Workspace/{key}", s_inputHashes.GetValueOrDefault(key), actual.GetValueOrDefault(key)))
				.Append(DescribeChange(constantsPath, ConstantsHash, HashOf(constantsPath)))
				.OfType<string>(),
		];

		Assert.IsEmpty(
			changed,

			"These inputs differ from the files the goldens were compiled from; restore them from git, because the goldens never change:\n"
				+ string.Join("\n", changed)
		);

		return TestWorkspace.CreateEmpty().CopyTreeFrom(root);
	}

	// Names an input whose hash differs from the one the goldens were compiled from; an input that matches gives null.
	private static string? DescribeChange(string name, string? expected, string? actual)
	{
		return (expected, actual) switch
		{
			(null, _) => $"{name}: added",
			(_, null) => $"{name}: removed",
			_ when string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) => null,
			_ => $"{name}: SHA-256 {actual}",
		};
	}

	private static string? HashOf(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;

	private static byte[] ReadGolden(int seed, string fileName) => File.ReadAllBytes(Path.Combine(TestFixtures.Vb6OutputDirectory(seed), fileName));
}
