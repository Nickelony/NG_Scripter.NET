using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Script;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
[TestCategory("Fast")]
public sealed class ScriptDatLanguageNameReaderTests
{
	// One section and one level path "a", laid out as VB6 writes them (Module1.bas:6661-6764), then the names (:6766-6773).
	private static byte[] ClassicPayload(params string[] languageNames)
	{
		byte[] classic = [0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 2, 0, 1, 0, .. new byte[40], 0, 0, (byte)'a', 0, 0, 0, 0x83];
		return [.. classic, .. languageNames.SelectMany(name => System.Text.Encoding.ASCII.GetBytes(name + "\0"))];
	}

	[TestMethod]
	public void Names_AreReadAfterTheSections()
	{
		// VB6 writes each language name with its terminator after the section bodies (Module1.bas:6766-6773).
		Assert.IsTrue(ScriptDatLanguageNameReader.TryRead(ClassicPayload("ENGLISH.DAT", "Italiano.dat"), out IReadOnlyList<string> names));
		Assert.AreSequenceEqual(["ENGLISH.DAT", "Italiano.dat"], names);
	}

	[TestMethod]
	public void Names_EndWhereTheNgHeaderStarts()
	{
		// VB6 appends the NG header, which ends with NGLE and its size, after the names (Module1.bas:8706).
		byte[] bytes = [.. ClassicPayload("ENGLISH.DAT"), .. NgWords.BuildHeaderBytes([0x0002, unchecked((short)0x8001)])];

		Assert.IsTrue(ScriptDatLanguageNameReader.TryRead(bytes, out IReadOnlyList<string> names));
		Assert.AreSequenceEqual(["ENGLISH.DAT"], names);
	}

	[TestMethod]
	public void Names_AreReadFromAScrambledHeader()
	{
		// VB6 scrambles the first 64 bytes of an encrypted script (CriptaScriptDat, Module1.bas:8720-8760), which hold the counts the reader
		// needs.
		byte[] bytes = ClassicPayload("ENGLISH.DAT", "FRENCH.DAT");
		Assert.IsTrue(LegacyCipher.TryScrambleScriptDatHeader(bytes));

		Assert.IsTrue(ScriptDatLanguageNameReader.TryRead(bytes, out IReadOnlyList<string> names));
		Assert.AreSequenceEqual(["ENGLISH.DAT", "FRENCH.DAT"], names);
	}

	[TestMethod]
	public void BytesThatHoldNoNameList_AreNoScriptDat()
	{
		// A name VB6 writes ends in .DAT (Module1.bas:6768), so another name, a missing list or a file shorter than the header holds none.
		Assert.IsFalse(ScriptDatLanguageNameReader.TryRead(ClassicPayload("ENGLISH.TXT"), out IReadOnlyList<string> wrongExtension));
		Assert.IsFalse(ScriptDatLanguageNameReader.TryRead(ClassicPayload(), out IReadOnlyList<string> noNames));
		Assert.IsFalse(ScriptDatLanguageNameReader.TryRead(new byte[63], out IReadOnlyList<string> tooShort));
		Assert.AreEqual((0, 0, 0), (wrongExtension.Count, noNames.Count, tooShort.Count));
	}
}
