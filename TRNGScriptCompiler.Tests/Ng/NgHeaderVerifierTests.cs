using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
[TestCategory("Fast")]
public sealed class NgHeaderVerifierTests
{
	private static readonly short s_options = unchecked((short)NgTagType.ScriptOptions);
	private static readonly short s_level = unchecked((short)NgTagType.ScriptLevel);

	private static readonly short s_optionsFlags = unchecked((short)((200 * 256) + 1));
	private static readonly short s_levelFlags = unchecked((short)((201 * 256) + 1));

	// An [Options] section with its flags mini-tag, a security block, a level section with Snow= and its flags, an import of one byte whose
	// size runs in a dword, and the zero word VB6 leaves after an import of 3 bytes.
	private static List<short> WellFormed()
	{
		return
		[
			5,
			s_options,
			s_optionsFlags,
			0x0021,
			0,
			4,
			unchecked((short)NgTagType.Security),
			0x1234,
			0x5678,
			7,
			s_level,
			(2 * 256) + 1,
			1,
			s_levelFlags,
			0x0048,
			0,
			unchecked((short)0x8000),
			4,
			unchecked((short)NgTagType.ImportFile),
			1,
			0,
		];
	}

	private static (bool Verified, UnitRun Unit) Verify(List<short> words, int expectedMiniTags)
	{
		var unit = new UnitRun();
		return (NgHeaderVerifier.Verify(words, expectedMiniTags, unit.Run.Diagnostics), unit);
	}

	[TestMethod]
	public void WellFormedHeader_CountsTheMiniTagsOfItsOptionsAndLevelSections()
	{
		// VB6 walks the fields by their size words, counts the mini-tags of the [Options] and level sections up to their zero word, and
		// compares the count with the mini-tags it wrote (VerificaHeaderNG, Module1.bas:8049-8105); a size with bit 15 set runs in a dword
		// (:6797-6810).
		(bool verified, UnitRun unit) = Verify(WellFormed(), expectedMiniTags: 3);

		Assert.IsTrue(verified);
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	public void MiniTagCountThatDiffersFromTheWrittenOne_Fails()
	{
		// VB6 fails a header whose mini-tags differ in number from those it wrote (Module1.bas:8097-8102).
		(bool verified, UnitRun unit) = Verify(WellFormed(), expectedMiniTags: 4);

		Assert.IsFalse(verified);
		DiagnosticAssert.SingleError(unit.Diagnostics, line: null, "The NG header holds 3 mini-tags, but 4 were written");
	}

	[TestMethod]
	public void FieldThatRunsPastTheHeader_Fails()
	{
		// VB6 reads each field by its size word (Module1.bas:8065, 8094), so a size past the end breaks the walk.
		(bool verified, UnitRun unit) = Verify([9, s_options, 0], expectedMiniTags: 0);

		Assert.IsFalse(verified);
		DiagnosticAssert.SingleError(unit.Diagnostics, line: null, "The NG header is corrupt, because its field boundaries do not parse");
	}

	[TestMethod]
	public void MiniTagThatRunsPastItsSection_Fails()
	{
		// VB6 fails a mini-tag whose word count carries it past the end of its section (Module1.bas:8074-8090).
		(bool verified, UnitRun unit) = Verify([4, s_level, (2 * 256) + 5, 1], expectedMiniTags: 1);

		Assert.IsFalse(verified);

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			line: null,
			"The NG header is corrupt, because the mini-tag 0x02 runs past the end of its section"
		);
	}
}
