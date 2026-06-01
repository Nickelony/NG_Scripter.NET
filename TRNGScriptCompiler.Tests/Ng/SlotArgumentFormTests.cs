using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

/// <summary>
/// The forms a <c>SLOT</c> argument takes: a number, a <c>$</c> literal, a slot name, a <c>#DEFINE</c> and sums
/// of them (PD-60).
/// </summary>
[TestClass]
public sealed class SlotArgumentFormTests
{
	[TestMethod]
	[TestCategory("IO")]
	[DataRow("AssignSlot= MY_SLOT,1234", "AssignSlot= 100,1234")]
	[DataRow("AssignSlot= LARA+5,1234", "AssignSlot= 5,1234")]
	[DataRow("AssignSlot= MY_SLOT+$10-PISTOLS_ANIM,1234", "AssignSlot= 115,1234")]
	[DataRow("Enemy= SKELETON,1,2,3,4", "Enemy= 35,1,2,3,4")]
	public void SlotNameDefineOrSum_CompilesToItsValue(string command, string numericCommand)
	{
		// VB6 takes a number, a $ literal or a slot name (Module1.bas:7432-7436, 7499-7506); a define and a sum read as clearly.
		byte[] written = CompileScriptDat(command);
		byte[] expected = CompileScriptDat(numericCommand);

		Assert.AreSequenceEqual(expected, written);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(
		"AssignSlot= PLANT1,1234",
		"Argument 1 of AssignSlot= names PLANT1, which is a static, not a slot; write a slot name, a number, a $ literal, a #DEFINE or a sum of them"
	)]
	[DataRow(
		"AssignSlot= SNOW_ALL_OUTSIDE,1234",
		"Argument 1 of AssignSlot= names SNOW_ALL_OUTSIDE, which is a TRNG constant, not a slot; write a slot name, a number, a $ literal, a #DEFINE or a sum of them"
	)]
	[DataRow(
		"Equipment= PLUGIN_DEMO_CONST,1",
		"Argument 1 of Equipment= names PLUGIN_DEMO_CONST, which is a plugin constant, not a slot; write a slot name, a number, a $ literal, a #DEFINE or a sum of them"
	)]
	[DataRow(
		"TestPosition= 1,0,LARA+ROCK1,0,0,0,0,0,0,0,0,0,0,0,0",
		"Argument 3 of TestPosition= names ROCK1, which is a static, not a slot; write a slot name, a number, a $ literal, a #DEFINE or a sum of them"
	)]
	[DataRow("AssignSlot= 0x1F,1234", "Argument 1 of AssignSlot= is invalid: 0x1F is not a number; write $1F for a hexadecimal value")]
	[DataRow("AssignSlot= #1F,1234", "Argument 1 of AssignSlot= writes #1F, a hexadecimal form a slot does not take; write $1F")]
	public void SlotOfAnotherForm_Fails(string command, string expectedMessage)
	{
		// VB6 fails on each with "unrecognized object name" (Module1.bas:7499-7513), because it looks the text up only among the slots.
		TestCompileResult result = Compile(command, out _);

		Assert.IsFalse(result.Success, result.Log);
		Assert.AreEqual(expectedMessage, result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message);
	}

	private static byte[] CompileScriptDat(string command) => TestWorkspace.CompileScriptDatOnStockObjectsH(Script(command), WithPlugin);

	private static TestCompileResult Compile(string command, out byte[] scriptDat) =>
		TestWorkspace.CompileOnStockObjectsH(Script(command), out scriptDat, WithPlugin);

	private static string Script(string command) => "#define MY_SLOT 100\n" + TestScripts.LanguageAndLevel + command + "\n";

	private static void WithPlugin(TestWorkspace workspace) =>
		workspace.WithPluginFile("PluginDemo.script", "<START_CONSTANTS>\r\nPLUGIN_DEMO_CONST:$40\r\n<END>\r\n");
}
