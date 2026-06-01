using System.Text;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

/// <summary>
/// An NG level section whose written word count, including its size and tag words, passes 32767 fails the compile,
/// as VB6 checks it (<c>Module1.bas:8559-8565</c>), instead of writing a negative count.
/// </summary>
[TestClass]
public sealed class NgLevelSectionSizeTests
{
	[TestMethod]
	[TestCategory("IO")]
	[DataRow(242, true)]
	[DataRow(243, false)]
	public void LevelSectionSize_IsCheckedOnTheWrittenCount(int lastArrayLength, bool expectedSuccess)
	{
		// 127 Animation= commands of 256 words each, one of 8 + lastArrayLength words, and the 3 words of the level flags
		// mini-tag make a payload of 32765 or 32766 words; the size and tag words add 2.
		var script = new StringBuilder(TestScripts.LanguageAndLevel);

		for (int id = 1; id <= 127; id++)
			script.Append(TestWorkspace.AnimationCommand(id, 248));

		script.Append(TestWorkspace.AnimationCommand(128, lastArrayLength));

		using var workspace = TestWorkspace.CreateCompilable(script.ToString());

		TestCompileResult result = workspace.Compile();

		Assert.AreEqual(expectedSuccess, result.Success, result.Log);

		if (expectedSuccess)
		{
			var reader = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat"));
			ScriptDatReader.NgField level = reader.GetNgField((ushort)NgTagType.ScriptLevel);

			Assert.AreEqual(short.MaxValue, level.DataWordCount + 2, "The section must sit exactly at the limit.");
		}
		else
		{
			Assert.Contains(
				diagnostic => diagnostic.IsError && diagnostic.Message == "NG level section 0 holds 32768 words; the format holds at most 32767",
				result.Diagnostics,
				result.Log
			);
		}
	}
}
