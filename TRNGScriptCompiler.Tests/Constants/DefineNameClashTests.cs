using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Constants;

/// <summary>
/// A <c>#DEFINE</c> cannot reuse the name of a constant, a plugin constant, a slot or a static (PD-35).
/// </summary>
[TestClass]
public sealed class DefineNameClashTests
{
	[TestMethod]
	[TestCategory("IO")]
	[DataRow("SNOW_ALL_OUTSIDE", "a TRNG constant")]
	[DataRow("PLUGIN_DEMO_CONST", "a plugin constant")]
	[DataRow("LARA", "a slot")]
	[DataRow("PLANT1", "a static")]
	public void DefineThatReusesAName_Fails(string name, string kind)
	{
		// VB6 looks up constants, plugin constants and slots before defines, and statics after them (Module1.bas:10117-10170), so the
		// define changes nothing, or hides the static, without a word.
		using var workspace = TestWorkspace.CreateCompilable($"#define {name} 1\n" + TestScripts.LanguageAndLevel);
		workspace.WithStockObjectsH();
		workspace.WithPluginFile("PluginDemo.script", "<START_CONSTANTS>\r\nPLUGIN_DEMO_CONST:$40\r\n<END>\r\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		Assert.AreEqual(
			$"#DEFINE {name} reuses the name of {kind}, and a name holds one value; rename the define",
			result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("OBJ_RUBBER_BOAT")]
	[DataRow("LARA")]
	[DataRow("PLANT1")]
	public void FailedDefineThatReusesAName_FailsOnItsOwnLineAlone(string name)
	{
		// VB6 looks the constants and the slots up before the defines, and the statics after them (Module1.bas:10117-10170), so a use of
		// the name reads the constant, slot or static, and only the #DEFINE line fails.
		using var workspace = TestWorkspace.CreateCompilable($"#DEFINE {name} 5+\n" + TestScripts.LanguageAndLevel + $"UVRotate= {name}\n");
		workspace.WithStockObjectsH();

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		Assert.AreSequenceEqual(
			["The #DEFINE value [5+] is invalid: 5+ has an operator with no value after it; write 5"],
			result.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message),
			result.Log
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void DefineWithAFreshName_Compiles()
	{
		using var workspace = TestWorkspace.CreateCompilable("#define MY_FOG 1\n" + TestScripts.LanguageAndLevel);
		workspace.WithStockObjectsH();

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
	}
}
