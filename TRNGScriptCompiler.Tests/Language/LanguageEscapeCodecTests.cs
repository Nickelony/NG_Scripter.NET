using TRNGScriptCompiler.Language;

namespace TRNGScriptCompiler.Tests.Language;

[TestClass]
[TestCategory("Fast")]
public sealed class LanguageEscapeCodecTests
{
	private static readonly string s_controlCharacter17 = ((char)0x17).ToString();
	private static readonly string s_controlCharacter14 = ((char)0x14).ToString();

	[TestMethod]
	public void TryDecodeClassic_DecodesNewlineAndTab()
	{
		Assert.IsTrue(LanguageEscapeCodec.TryDecodeClassic("line1\\nline2\\tend", out string text, out _));
		Assert.AreEqual("line1\nline2\tend", text);
	}

	[TestMethod]
	[DataRow("A\\x42", "AB")]
	[DataRow("A\\x6a", "Aj")]
	[DataRow("A\\x4", "A\u0004")]
	public void TryDecodeClassic_ReadsOneOrTwoHexDigitsOfEitherCase(string escaped, string expected)
	{
		// VB6 reads the two characters after \x, or the one before the end of the text, as hexadecimal (Module1.bas:2205-2222).
		Assert.IsTrue(LanguageEscapeCodec.TryDecodeClassic(escaped, out string text, out _));
		Assert.AreEqual(expected, text);
	}

	[TestMethod]
	[DataRow("A\\xG1")]
	[DataRow("A\\x4g")]
	[DataRow("A\\x")]
	[DataRow("A\\x+1")]
	public void TryDecodeClassic_RejectsAnEscapeWithoutHexDigits(string escaped) =>
		Assert.IsFalse(LanguageEscapeCodec.TryDecodeClassic(escaped, out _, out _));

	[TestMethod]
	public void TryDecodeClassic_StripsCommentNotationAfterTrim()
	{
		// VB6 cuts the text at the ';' and keeps the space before it (Module1.bas:2338-2341); the port trims it too (PD-48).
		Assert.IsTrue(LanguageEscapeCodec.TryDecodeClassic("  Hello ; note  ", out string text, out _));
		Assert.AreEqual("Hello", text);
	}

	[TestMethod]
	public void TryDecodeClassic_ExtractsALeadingTagWithoutSpaces()
	{
		// VB6 takes the text before the first colon as a special tag when it holds no space (Module1.bas:2244-2263).
		Assert.IsTrue(LanguageEscapeCodec.TryDecodeClassic("Tag: value", out string text, out string specialTag));
		Assert.AreEqual(("Tag:", "value"), (specialTag, text));

		Assert.IsTrue(LanguageEscapeCodec.TryDecodeClassic("Two words: value", out text, out specialTag));
		Assert.AreEqual((string.Empty, "Two words: value"), (specialTag, text));
	}

	[TestMethod]
	public void TryDecodeExtraNg_KeepsLeadingTag()
	{
		// VB6 leaves StringaC_To_Abs before the tag for an ExtraNG string (Module1.bas:2235-2240), so "Tag:" remains text.
		Assert.IsTrue(LanguageEscapeCodec.TryDecodeExtraNg("Tag: value", out string text));
		Assert.AreEqual("Tag: value", text);
	}

	[TestMethod]
	public void Encode_EncodesControlCharacters()
	{
		Assert.AreEqual("a\\nb\\tc", LanguageEscapeCodec.Encode("a\nb\tc"));
		Assert.AreEqual("\\x17", LanguageEscapeCodec.Encode(s_controlCharacter17));
		Assert.AreEqual("\\x14", LanguageEscapeCodec.Encode(s_controlCharacter14));
		Assert.AreEqual("plain text", LanguageEscapeCodec.Encode("plain text"));
	}

	[TestMethod]
	public void Encode_AndDecode_RoundTrips()
	{
		string original = "line1\nline2\twith " + s_controlCharacter17 + " control";

		Assert.IsTrue(LanguageEscapeCodec.TryDecodeClassic(LanguageEscapeCodec.Encode(original), out string decoded, out _));
		Assert.AreEqual(original, decoded);
	}

	[TestMethod]
	public void EscapeDecoding_MatchesVb6Semantics()
	{
		// \xNN decoding (case-insensitive).
		Assert.IsTrue(LanguageEscapeCodec.TryDecodeClassic("A\\x41B", out string text, out _));
		Assert.AreEqual("AAB", text);

		// Replacements cascade: \x5c becomes '\', which re-forms a \t escape.
		Assert.IsTrue(LanguageEscapeCodec.TryDecodeClassic("\\x5ct", out text, out _));
		Assert.AreEqual("\t", text);

		// Backslashes are not escapes in VB6: \\ survives as two characters, and the
		// second backslash plus 'n' still forms a \n escape. "C:" has no space before
		// its colon, so VB6 also extracts it as a special tag ("5C 0A 65 77" = \, LF, e, w).
		Assert.IsTrue(LanguageEscapeCodec.TryDecodeClassic("C:" + new string('\\', 2) + "new", out text, out string specialTag));
		Assert.AreEqual("C:", specialTag);

		Assert.AreEqual(
			"5C 0A 65 77",
			string.Join(' ', text.Select(ch => ((int)ch).ToString("X2", System.Globalization.CultureInfo.InvariantCulture))),
			text
		);

		// Invalid hex after \x fails the conversion (VB6 aborts the language file).
		Assert.IsFalse(LanguageEscapeCodec.TryDecodeClassic("\\xzz", out _, out _));
	}

	[TestMethod]
	public void ChainOfEscapes_IsRescannedUntilOneBackslashIsLeft()
	{
		// Each \x5c becomes a backslash that joins the x5c after it into a new escape, so VB6 scans again (Module1.bas:2205-2229).
		string chain = "\x5c" + string.Concat(Enumerable.Repeat("x5c", 2_000));

		Assert.IsTrue(LanguageEscapeCodec.TryDecodeExtraNg(chain, out string text));
		Assert.AreEqual("\\", text);
	}
}
