using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
[TestCategory("Fast")]
public sealed class RepeatedSettingMessagesTests
{
	private static readonly string s_script = Path.Combine(path1: TestWorkspace.TempRoot, path2: "script", path3: "Script.txt");

	private static readonly string s_include = Path.Combine(path1: TestWorkspace.TempRoot, path2: "script", path3: "Rooms.txt");

	[TestMethod]
	public void EarlierLine_NamesItsFileOnlyWhenTheFilesDiffer()
	{
		// VB6 numbers the lines of each file from 1 (Module1.bas:5191-5198), so a line of an include names its file.
		Assert.AreEqual(
			"'Fog=' gives a value different from line 4, and the setting holds one value; keep one of the two lines",
			RepeatedSettingMessages.DifferentValue(subject: "Fog=", new SourceLocation(4, File: s_script), currentFile: s_script.ToUpperInvariant())
		);

		Assert.AreEqual(
			"'Fog=' repeats line 4 of Rooms.txt with the same value; remove one of the two lines",
			RepeatedSettingMessages.SameValue(subject: "Fog=", new SourceLocation(4, File: s_include), currentFile: s_script)
		);
	}

	[TestMethod]
	public void SecondLevelLine_NamesTheFirstLevelLine()
	{
		// VB6 adds a level path for each Level= (Module1.bas:5809-5814), so the message names the first one.
		Assert.AreEqual(
			"The section gives 'Level=' at line 3 of Rooms.txt already, and a second level path would point every later level at the wrong file; keep one of the two lines",
			RepeatedSettingMessages.SecondLevelLine(new SourceLocation(3, File: s_include), s_script)
		);
	}
}
