using System.Text;
using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

/// <summary>
/// The range of each NG argument, of an NG array and of an NG command, against the field that holds it.
/// </summary>
[TestClass]
public sealed class NgArgumentRangeTests
{
	[TestMethod]
	[TestCategory("IO")]
	[DataRow("AssignSlot= 465,1234")]
	[DataRow("AssignSlot= 490,1234")]
	[DataRow("AssignSlot= FROG_MAN_HARPOON,1234")]
	[DataRow("Equipment= $1EA,1")]
	[DataRow("Enemy= 490,1,2,3,4")]
	[DataRow("AnimationSlot= 490,1,2,3,4,5,6,7,8")]
	public void SlotThatTheStockObjectsHDefines_Compiles(string command)
	{
		// VB6 width-checks a numeric slot and looks a name up in object_types (Module1.bas:7432-7436, 7499-7506); the stock
		// Objects.h defines slots 0 to 490, before NUMBER_OBJECTS.
		TestCompileResult result = TestWorkspace.CompileOnStockObjectsH(TestScripts.LanguageAndLevel + command + "\n", out _);
		Assert.IsTrue(result.Success, result.Log);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("AssignSlot= 491,1234", "Argument 1 of AssignSlot= names slot 491, but Objects.h defines slots 0 to 490")]
	[DataRow("Equipment= NUMBER_OBJECTS,1", "Argument 1 of Equipment= names slot 491, but Objects.h defines slots 0 to 490")]
	[DataRow("Enemy= -1,1,2,3,4", "Argument 1 of Enemy= names slot -1, but Objects.h defines slots 0 to 490")]
	[DataRow("AssignSlot= IGNORE,1234", "Argument 1 of AssignSlot= names slot -1, but Objects.h defines slots 0 to 490")]
	public void SlotOutsideTheObjectsHSlots_Fails(string command, string expectedMessage)
	{
		// PD-56: VB6 writes any slot its two-byte width check passes (Module1.bas:7432-7436), so slot 491 reaches the engine.
		TestCompileResult result = TestWorkspace.CompileOnStockObjectsH(TestScripts.LanguageAndLevel + command + "\n", out _);

		Assert.IsFalse(result.Success, result.Log);
		Assert.AreEqual(expectedMessage, result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("TestPosition= 1,TPOS_TEST_ITEM_INDEX,1000,0,0,0,0,0,0,0,0,0,0,0,0", true)]
	[DataRow("TestPosition= 1,0,65535,0,0,0,0,0,0,0,0,0,0,0,0", true)]
	[DataRow("TestPosition= 1,0,65536,0,0,0,0,0,0,0,0,0,0,0,0", false)]
	public void TestPositionSlotField_TakesTheWordRange(string command, bool compiles)
	{
		// With TPOS_TEST_ITEM_INDEX the slot field holds an item index, so it keeps the range of a word.
		TestCompileResult result = TestWorkspace.CompileOnStockObjectsH(TestScripts.LanguageAndLevel + command + "\n", out _);

		Assert.AreEqual(compiles, result.Success, result.Log);

		if (!compiles)
		{
			DiagnosticAssert.SingleError(
				result.Diagnostics,
				7,
				"Argument 3 of TestPosition= is out of range: 65536 lies outside -32768 to 65535, the values its word holds"
			);
		}
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("KeyPad= -32768,65535,-1,IGNORE", null)]
	[DataRow("KeyPad= 65536,0,0,0", "Argument 1 of KeyPad= is out of range: 65536 lies outside -32768 to 65535, the values its word holds")]
	[DataRow("KeyPad= 0,-32769,0,0", "Argument 2 of KeyPad= is out of range: -32769 lies outside -32768 to 65535, the values its word holds")]
	[DataRow("ItemGroup= 1,2,3,-40000", "Argument 4 of ItemGroup= is out of range: -40000 lies outside -32768 to 65535, the values its word holds")]
	[DataRow("TextureSequence= 1,2,3,15,0", null)]
	[DataRow(
		"TextureSequence= 1,2,3,16",
		"Argument 4 of TextureSequence= is out of range: 16 lies outside 0 to 15, the values an array nybble holds"
	)]
	[DataRow(
		"TextureSequence= 1,2,3,-1",
		"Argument 4 of TextureSequence= is out of range: -1 lies outside 0 to 15, the values an array nybble holds"
	)]
	public void WordAndNybbleValues_TakeTheRangeOfTheirField(string command, string? expectedError)
	{
		// PD-56: VB6 width-checks a word to -65536..65535 (Module1.bas:4915-4937), so -40000 is written as 25536; it keeps a
		// nybble to 0-15 (Module1.bas:7241-7246).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + command + "\n");

		TestCompileResult result = workspace.Compile();

		if (expectedError is null)
			Assert.IsTrue(result.Success, result.Log);
		else
			DiagnosticAssert.SingleError(result.Diagnostics, 7, expectedError);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("255", true)]
	[DataRow("256", false)]
	[DataRow("-1", false)]
	public void ByteArrayValue_TakesTheRangeOfAByte(string value, bool parses)
	{
		// Module1.bas:7230-7235 keeps a byte-array value to 0-255; no stock command takes a byte array, so a plugin's definition stands in.
		(List<NgArgumentValue>? arguments, DiagnosticSink diagnostics) = ParseByteArrayCommand(["1", value]);

		Assert.AreEqual(parses, arguments is not null);

		if (!parses)
		{
			Assert.AreEqual(
				$"Argument 2 of PluginBytes= is out of range: {value} lies outside 0 to 255, the values an array byte holds",
				diagnostics.Recorded.Single().Message
			);
		}
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow(255, true)]
	[DataRow(256, false)]
	public void ByteArrayOf256Values_Fails(int count, bool parses)
	{
		// The count of a byte array is one byte, and VB6 overflows at 256 (Module1.bas:7195).
		(List<NgArgumentValue>? arguments, DiagnosticSink diagnostics) = ParseByteArrayCommand(["1", .. Enumerable.Repeat("7", count)]);

		Assert.AreEqual(parses, arguments is not null);

		if (!parses)
		{
			Assert.AreEqual(
				"PluginBytes= gives 256 array values, but the array holds at most 255, because its count is one byte",
				diagnostics.Recorded.Single().Message
			);
		}
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NybbleArrayOf256Values_Fails()
	{
		string nybbles = string.Join(',', Enumerable.Repeat(1, 256));
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + $"TextureSequence= 1,2,3,{nybbles}\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			result.Diagnostics,
			7,
			"TextureSequence= gives 256 array values, but the array holds at most 255, because its count is one byte"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(253, true)]
	[DataRow(254, false)]
	public void CommandOfMoreThan255Words_Fails(int arrayLength, bool compiles)
	{
		// The low byte of the header word counts the argument words, and VB6 rejects 256 or more (Module1.bas:7358-7362).
		// ItemGroup= writes its two fixed words and one word per array value.
		string values = string.Join(',', Enumerable.Repeat(1, arrayLength));
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + $"ItemGroup= 1,2,{values}\n");

		TestCompileResult result = workspace.Compile();

		Assert.AreEqual(compiles, result.Success, result.Log);

		if (!compiles)
		{
			DiagnosticAssert.SingleError(
				result.Diagnostics,
				7,
				"ItemGroup= writes 256 words of arguments, but a command holds at most 255; give fewer values"
			);
		}
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(245, true)]
	[DataRow(246, false)]
	public void OptionsSectionOfMoreThan32767Words_Fails(int lastArrayLength, bool compiles)
	{
		// VB6 stores the size of the options section in an Integer, which overflows past 32767 (Module1.bas:8407). 127 Plugin=
		// commands of 256 words, one of 5 + lastArrayLength words, the size and tag words and the 3 words of the flags mini-tag
		// make 32767 or 32768 words.
		var script = new StringBuilder(TestScripts.Language + "[Options]\n");
		var language = new StringBuilder(TestWorkspace.CompilableLanguageContent + "[PSXStrings]\n[PCStrings]\n[ExtraNG]\n");

		for (int id = 1; id <= 128; id++)
		{
			int arrayLength = id <= 127 ? 251 : lastArrayLength;

			script.Append($"Plugin= {id},Plugin_{id},0,{string.Join(',', Enumerable.Repeat(1, arrayLength))}\n");
			language.Append($"{id}: Plugin_{id}\n");
		}

		script.Append(TestScripts.Level);
		language.Append("THE LAST STRING ENTRY\n");

		using var workspace = TestWorkspace.CreateCompilable(script.ToString(), language.ToString());

		TestCompileResult result = workspace.Compile();

		Assert.AreEqual(compiles, result.Success, result.Log);

		if (compiles)
		{
			var reader = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat"));
			Assert.AreEqual(short.MaxValue, reader.GetNgField((ushort)NgTagType.ScriptOptions).DataWordCount + 2);
		}
		else
		{
			// The error points at the [Options] line, as the level section's error points at its [Level] line.
			DiagnosticAssert.SingleError(
				diagnostics: result.Diagnostics,
				line: 3,
				message: "The NG options section holds 32768 words; the format holds at most 32767",
				file: workspace.PathOf(Path.Combine("script", "Script.txt"))
			);
		}
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("TriggerGroup= 1,$2000,406,1,2", "TriggerGroup= ends in a partial group of 1 value; each exported trigger takes 3 values")]
	[DataRow("MultEnvCondition= 1,2,3,4,5,6", "MultEnvCondition= ends in a partial group of 2 values; each ENV_ condition takes 3 values")]
	[DataRow("Organizer= 1,2,3,4,5,6", "Organizer= ends in a partial group of 1 value; each time and TriggerGroup pair takes 2 values")]
	public void GroupedArrayEndingInAPartialGroup_Warns(string command, string expectedWarning)
	{
		// VB6 counts an array as however many arguments remain (Module1.bas:7154-7172), so a partial group compiles; the help reads
		// these commands in groups (help_new_script_command.txt:3252, 2334, 2410).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + command + "\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		DiagnosticAssert.HasWarning(result.Diagnostics, 7, expectedWarning);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("TriggerGroup= 1,$2000,406,1")]
	[DataRow("TriggerGroup= 1,$2000,406,1,$2000,406,2")]
	[DataRow("MultEnvCondition= 1,2,3,4,5,6,7")]
	[DataRow("Organizer= 1,2,3,4,5")]
	[DataRow("Organizer= 1,2,3,4,5,6,7")]
	public void GroupedArrayOfWholeGroups_DoesNotWarn(string command)
	{
		using var workspace = TestWorkspace.CreateClean(command + "\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		DiagnosticAssert.NoDiagnostics(result.Diagnostics);
	}

	private static (List<NgArgumentValue>? Arguments, DiagnosticSink Diagnostics) ParseByteArrayCommand(IReadOnlyList<string> arguments)
	{
		var logger = new CompilerLog(conciseLogging: false);
		var diagnostics = new DiagnosticSink(logger);
		var parser = new NgArgumentParser(diagnostics, new ObjectsHCatalog(logger, diagnostics), new ExpressionEvaluator((_, _) => default));

		var definition = new NgCommandDefinition
		{
			Name = "PluginBytes=",
			Code = (NgCommandCode)250,
			ArgumentTypes = [NgArgumentType.Word, NgArgumentType.ArrayByte],
		};

		string text = $"PluginBytes= {string.Join(',', arguments)}";

		return (
			parser.Parse(
				definition,
				new ScriptCommandLine(text, SourceText: text, Command: "PluginBytes=", arguments, Location: new SourceLocation(1))
			),
			diagnostics
		);
	}
}
