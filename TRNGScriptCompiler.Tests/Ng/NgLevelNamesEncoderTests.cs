using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
[TestCategory("Fast")]
public sealed class NgLevelNamesEncoderTests
{
	private static ScriptModel Script(params string[] levelPaths) =>
		new(new ParsedScript { Sections = [.. levelPaths.Select(path => new ParsedSection { LevelPath = path })] }, new NgScriptModel());

	private static List<short> Encode(ScriptModel script)
	{
		var words = new List<short>();
		NgLevelNamesEncoder.Encode(words, script);
		return words;
	}

	[TestMethod]
	public void LevelPaths_AreJoinedByBars_AndPaddedToAnEvenLength()
	{
		// VB6 joins the level paths with '|', adds a space to an odd length, and writes the word count, the tag, the length and the bytes
		// (Module1.bas:8654-8694).
		short[] even = [(20 / 2) + 3, unchecked((short)NgTagType.LevelNames), 20, .. Packed(@"data\title|data\test")];
		short[] odd = [(4 / 2) + 3, unchecked((short)NgTagType.LevelNames), 4, .. Packed("a|b ")];

		Assert.AreSequenceEqual(even, Encode(Script(@"data\title", "", @"data\test")));
		Assert.AreSequenceEqual(odd, Encode(Script("a", "b")));
	}

	[TestMethod]
	public void ScriptWithoutALevelPath_WritesNothing()
	{
		// VB6 builds the chunk from the Level= paths alone (Module1.bas:8657-8662), and the port writes none for an empty list.
		Assert.IsEmpty(Encode(Script("")));
	}

	private static short[] Packed(string text) =>
		[.. Enumerable.Range(0, text.Length / 2).Select(index => (short)(text[2 * index] | (text[(2 * index) + 1] << 8)))];
}
