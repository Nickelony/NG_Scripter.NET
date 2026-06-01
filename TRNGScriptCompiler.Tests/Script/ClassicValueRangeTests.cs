using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
public sealed class ClassicValueRangeTests
{
	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Fog= -1,0,0", "Argument 1 of Fog= is out of range: -1 lies outside 0 to 255, the values a color holds")]
	[DataRow("Mirror= -1,0", "Argument 1 of Mirror= is out of range: -1 lies outside 0 to 255, the values a room number holds")]
	[DataRow("Mirror= 1,-1", "Argument 2 of Mirror= is out of range: -1 lies outside 0 to 2147483647, the values a mirror origin holds")]
	[DataRow("UVRotate= -200", "Argument 1 of UVRotate= is out of range: -200 lies outside -128 to 255, the values its byte holds")]
	[DataRow("Layer1= 1,2,3,-129", "Argument 4 of Layer1= is out of range: -129 lies outside -128 to 255, the values its byte holds")]
	[DataRow("Layer1= -1,2,3,4", "Argument 1 of Layer1= is out of range: -1 lies outside 0 to 255, the values a color holds")]
	[DataRow("ResetHUB= -1", "Argument 1 of ResetHUB= is out of range: -1 lies outside 0 to 255, the values a level number holds")]
	[DataRow("LoadCamera= 0,0,0,0,0,0,-1", "Argument 7 of LoadCamera= is out of range: -1 lies outside 0 to 255, the values a room number holds")]
	[DataRow("FMV= 128,0", "The FMV= index must be between 0 and 127, because bit 7 of its byte is the trigger flag: 128")]
	[DataRow("FMV= 1,2", "The FMV= trigger must be 0 or 1: 2")]
	[DataRow(
		"LensFlare= 8388608,0,0,0,0,0",
		"Coordinate 1 of LensFlare= is out of range: 8388608 divided by 256 must fit -32768 to 32767, so the coordinate lies between -8388863 and 8388607"
	)]
	[DataRow(
		"LensFlare= 0,-8388864,0,0,0,0",
		"Coordinate 2 of LensFlare= is out of range: -8388864 divided by 256 must fit -32768 to 32767, so the coordinate lies between -8388863 and 8388607"
	)]
	[DataRow(
		"Key= 1,Test Level,-32769,0,0,0,0,0",
		"Argument 3 of Key= is out of range: -32769 lies outside -32768 to 65535, the values its word holds"
	)]
	[DataRow("AnimatingMIP= 1,16", "Argument 2 of AnimatingMIP= is out of range: 16 lies outside 0 to 15, the values a distance holds")]
	[DataRow("AnimatingMIP= 1,20\nFog= 1,2,3", "Argument 2 of AnimatingMIP= is out of range: 20 lies outside 0 to 15, the values a distance holds")]
	public void ClassicValueOutsideItsField_Fails(string commandLine, string expectedMessage)
	{
		// PD-56: VB6 width-checks a byte to -256..255 (Module1.bas:4915-4937), so -1 and -200 wrap; it writes an FMV= index
		// above 127 into the trigger bit (:5789-5790), a LensFlare= quotient outside the signed word wrapped (:6028-6034),
		// and checks the AnimatingMIP= distance only when the command ends its section (:6141-6148).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + commandLine + "\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, $"'{commandLine}' must fail: {result.Log}");
		DiagnosticAssert.SingleError(diagnostics: result.Diagnostics, line: 7, message: expectedMessage, file: workspace.ScriptFile);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NegativeLayerSpeed_IsWrittenAsItsByte()
	{
		// The Layer1= speed is signed, and a negative speed reverses the movement. Layer1= writes tag 0x89 and four bytes
		// (Module1.bas:5934-5951), so -128 is 0x80, the byte 128 writes too.
		using var negative = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "Layer1= 1,2,3,-128\n");
		using var unsigned = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "Layer1= 1,2,3,128\n");

		TestCompileResult negativeResult = negative.Compile();
		TestCompileResult unsignedResult = unsigned.Compile();

		Assert.IsTrue(negativeResult.Success, negativeResult.Log);
		Assert.IsTrue(unsignedResult.Success, unsignedResult.Log);

		Assert.AreSequenceEqual(
			new byte[] { 0x89, 0x01, 0x02, 0x03, 0x80 },
			ScriptDatReader.Read(negative.ReadScriptDat()).GetSectionBytes(0).Take(5),
			"The speed -128 must be written as 0x80."
		);

		Assert.AreSequenceEqual(unsigned.ReadScriptDat(), negative.ReadScriptDat());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LensFlareCoordinatesAtTheEndsOfTheWord_AreWritten()
	{
		// VB6 reads a LensFlare= coordinate 4 bytes wide and writes it divided by 256 as a word (Module1.bas:6028-6034), and the
		// division truncates toward zero: 8388607 is 0x7FFF, -8388863 is 0x8000 and -257 is 0xFFFF.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "LensFlare= 8388607,-8388863,-257,4,5,6\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		Assert.AreSequenceEqual(
			new byte[] { 0x8D, 0xFF, 0x7F, 0x00, 0x80, 0xFF, 0xFF, 0x04, 0x05, 0x06 },
			ScriptDatReader.Read(workspace.ReadScriptDat()).GetSectionBytes(0).Take(10)
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(
		"Name= Far String\n",
		4,
		"The Name= string 'Far String' is entry 256 of the language file, but Name= writes its index in one byte; move the string into the first 256 entries"
	)]
	[DataRow(
		"Name= Test Level\nLegend= Far String\n",
		5,
		"The Legend= string 'Far String' is entry 256 of the language file, but Legend= writes its index in one byte; move the string into the first 256 entries"
	)]
	public void StringIndexPastTheFirst256_Fails(string lines, int expectedLine, string expectedMessage)
	{
		// PD-56: Name= and Legend= write their string index in one byte (Module1.bas:5851, 5994), so VB6 shows string 0 for string 256.
		string script = TestScripts.Language + "[Level]\n" + lines + "Level= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n";
		string language = "[Strings]\nTest Level\n" + string.Concat(Enumerable.Range(1, 255).Select(i => $"Filler {i}\n")) + "Far String\n";

		using var workspace = TestWorkspace.CreateCompilable(script, language);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "String 256 does not fit the one-byte index.");
		DiagnosticAssert.SingleError(diagnostics: result.Diagnostics, line: expectedLine, message: expectedMessage, file: workspace.ScriptFile);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void StringIndex255_IsWritten()
	{
		const string script = TestScripts.Language + "[Level]\nName= Far String\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n";
		string language = "[Strings]\nTest Level\n" + string.Concat(Enumerable.Range(1, 254).Select(i => $"Filler {i}\n")) + "Far String\n";

		using var workspace = TestWorkspace.CreateCompilable(script, language);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		// The section ends with its header: the tag 0x81, the name index, the flags word, the level index, the CD slot and 0x83.
		Assert.AreSequenceEqual(
			new byte[] { 0x81, 0xFF },
			ScriptDatReader.Read(workspace.ReadScriptDat()).GetSectionBytes(0).TakeLast(7).Take(2),
			"The level header must name string 255."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ExtensionSectionWithAMissingLowerSlot_Fails()
	{
		// PD-57: VB6 writes slots 0 to count-1, where the count is the number of distinct commands (Module1.bas:5545-5562,
		// 6714-6718), so a section with Level= and Cut= but no FMV= writes Level= and an empty FMV= slot and drops Cut=. The error points
		// at the Cut= line.
		using var workspace = TestWorkspace.CreateCompilable("[PCExtensions]\nLevel= .TR4\nCut= .TR4\n" + TestScripts.LanguageAndLevel);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "The Cut= slot would be dropped.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 3,
			message: "The [PCExtensions] section declares 'Cut=' but no 'FMV=', so the slot of 'Cut=' would be dropped; add a 'FMV=' line",
			file: workspace.PathOf(Path.Combine("script", "Script.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OverWidthByteArgument_Fails()
	{
		const string script = TestScripts.LanguageAndLevel + "Fog= 300,0,0\n";

		using var workspace = TestWorkspace.CreateCompilable(script, TestWorkspace.CompilableLanguageContent);
		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "VB6 errors when a value does not fit its declared width.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 7,
			message: "Argument 1 of Fog= is out of range: 300 lies outside -128 to 255, the values its byte holds",
			file: workspace.ScriptFile
		);
	}
}
