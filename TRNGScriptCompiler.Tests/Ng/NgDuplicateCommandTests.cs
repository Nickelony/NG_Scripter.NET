using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

/// <summary>The arguments that make a second line of an NG command in one section a duplicate.</summary>
[TestClass]
public sealed class NgDuplicateCommandTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void AnimationSlotLinesForOneSlotWithDifferentAnimations_AreBothWritten()
	{
		// PD-63: VB6 keys the duplicate check of AnimationSlot= (code 40, not in VetIgnora) on the slot alone (Module1.bas:1241,
		// 7594-7605), so it rejects the second line, while the help allows 511 per level (help_new_script_command.txt:794-805). Each line
		// writes the header 0x2809, code 40 with nine words, then the slot 35, its flags and its animation index.
		IReadOnlyList<ScriptDatReader.MiniTag> tags = NgArgumentEncodingTests.CompileLevelCommand(
			"AnimationSlot= 35,0,100,0,0,0,0,0,0\nAnimationSlot= 35,1,101,0,0,0,0,0,0"
		);

		short[][] animations = [.. tags.Where(tag => tag.Code == 40).Select(tag => tag.Words)];

		Assert.HasCount(2, animations, "Both lines must be written.");
		Assert.AreSequenceEqual(new short[] { 35, 0, 100, 0, 0, 0, 0, 0, 0 }, animations[0], "The first animation must be written.");
		Assert.AreSequenceEqual(new short[] { 35, 1, 101, 0, 0, 0, 0, 0, 0 }, animations[1], "The second animation must be written.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void AnimationSlotLinesForOneSlotAndOneAnimation_AreADuplicate()
	{
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.LanguageAndLevel + "AnimationSlot= 35,0,100,0,0,0,0,0,0\nAnimationSlot= 35,1,100,0,0,0,0,0,0\n"
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);

		Assert.AreEqual(
			("Duplicated command [AnimationSlot= 35, 1, 100, ...] was already present in same [level/title/option] section", 8),
			(diagnostic.Message, diagnostic.LineNumber)
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Text A", "Text B", true)]
	[DataRow("Text A", "Text A", false)]
	[DataRow("Text B", "!100", true)]
	public void StringFirstArgument_IsKeyedOnTheIndexItResolvesTo(string first, string second, bool compiles)
	{
		// VB6 checks a duplicate on the value it read for the argument, which for a STRING is the resolved index (Module1.bas:7592-7604).
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.LanguageAndLevel + $"StringUser= {first}\nStringUser= {second}\n",
			"[Strings]\nTest Level\nText A\nText B\n\n[ExtraNG]\n100: Text C\n"
		);

		string stockConstants = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripter_constants.txt"));

		string constantsPath = workspace
			.WithTextFile("custom_constants.txt", stockConstants + "\r\n<START_NEW_COMMANDS>\r\n250#9#StringUser=:String\r\n<END>\r\n")
			.PathOf("custom_constants.txt");

		TestCompileResult result = workspace.Compile(workspace.CreateOptions() with { ConstantsFilePath = constantsPath });

		Assert.AreEqual(compiles, result.Success, result.Log);

		if (!compiles)
		{
			Assert.AreEqual(
				"Duplicated command [StringUser= 1, ...] was already present in same [level/title/option] section",
				result.Diagnostics.Single(entry => entry.IsError).Message
			);
		}
	}
}
