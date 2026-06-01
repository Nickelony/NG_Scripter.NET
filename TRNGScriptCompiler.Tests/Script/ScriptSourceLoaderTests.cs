using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
[TestCategory("Fast")]
public sealed class ScriptSourceLoaderTests
{
	private const string SplitsAValueOrAWord =
		"A '>' continuation joins the next line with nothing between, so here it splits a value or a word; break the line next to a comma or right after the '='";

	private const string CommentInName =
		"The #INCLUDE name holds a ';' or '//', where the line's comment starts and cuts the name, so an #INCLUDE name cannot hold either; rename the file";

	private const string UnclosedQuote = "The #INCLUDE name opens a double quote that is never closed; close it or remove it";

	/// <summary>
	/// Reads every line, pushing each <c>#INCLUDE</c> where it stands, until the reading ends or fails.
	/// </summary>
	private static (List<ScriptSourceLine> Lines, ScriptSourceLoader Loader, string? IncludeError) Read(UnitRun workspace)
	{
		ScriptSourceLoader loader = workspace.CreateLoader();
		var lines = new List<ScriptSourceLine>();

		while (loader.HasPendingFiles)
		{
			if (!loader.TryReadNext(out ScriptSourceLine? line))
				return (lines, loader, null);

			if (line.Text.StartsWith("#INCLUDE", StringComparison.OrdinalIgnoreCase))
			{
				if (!loader.TryPushInclude(line.Text, out string? error, out _))
					return (lines, loader, error);

				continue;
			}

			lines.Add(line);
		}

		return (lines, loader, null);
	}

	private static List<ScriptSourceLine> ReadAll(UnitRun workspace)
	{
		(List<ScriptSourceLine> lines, ScriptSourceLoader loader, string? includeError) = Read(workspace);

		Assert.IsNull(loader.Error, loader.Error?.Message);
		Assert.IsNull(includeError, includeError);

		return lines;
	}

	private static void AssertReadError(string script, int expectedLine, string expectedMessage)
	{
		var workspace = UnitRun.ForScript(script);
		(_, ScriptSourceLoader loader, _) = Read(workspace);

		Assert.IsNotNull(loader.Error);

		Assert.AreEqual(
			(expectedMessage, expectedLine, workspace.ScriptFile),
			(loader.Error.Message, loader.Error.Location.Line, loader.Error.Location.File)
		);
	}

	private static string IncludeError(UnitRun workspace)
	{
		(_, ScriptSourceLoader loader, string? includeError) = Read(workspace);

		Assert.IsNull(loader.Error, loader.Error?.Message);
		Assert.IsNotNull(includeError);

		return includeError;
	}

	[TestMethod]
	[DataRow("Fog= 1,2,>\n3\n", "Fog= 1,2,3")]
	[DataRow("Fog= 1,2>\n,3\n", "Fog= 1,2,3")]
	[DataRow("Fog= 1,>\n2,>\n3\n", "Fog= 1,2,3")]
	[DataRow("Fog=>\n1,2,3\n", "Fog=1,2,3")]
	[DataRow("Fog= 1,2,> ; the blue\n3\n", "Fog= 1,2,3")]
	[DataRow("  Fog= 1,2,>  \n   3   \n", "Fog= 1,2,3")]
	public void Continuation_JoinsTheTrimmedPiecesWithNothingBetween(string script, string expectedLine)
	{
		// VB6 cuts each piece at its first '>' before a ';', trims its spaces and joins it to the next line with nothing between
		// (Module1.bas:5149-5167).
		List<ScriptSourceLine> lines = ReadAll(UnitRun.ForScript(script));
		Assert.AreEqual(expectedLine, lines.Single().Text);
	}

	[TestMethod]
	public void JoinedLine_TakesItsFirstPhysicalLine_AndTheLinesAfterItKeepTheirNumbers()
	{
		// VB6 numbers a joined line by its first physical line (Glob.NLinea, Module1.bas:5147-5148) and counts each line it reads (:5151).
		List<ScriptSourceLine> lines = ReadAll(UnitRun.ForScript("[Level]\nFog= 1,>\n2,>\n3\nSnow= 1\n"));

		Assert.AreSequenceEqual([1, 2, 5], lines.Select(line => line.Location.Line));
		Assert.AreSequenceEqual(["[Level]", "Fog= 1,2,3", "Snow= 1"], lines.Select(line => line.Text));
	}

	[TestMethod]
	public void SourceText_IsTheFirstPhysicalLineAsTheFileHoldsIt()
	{
		List<ScriptSourceLine> lines = ReadAll(UnitRun.ForScript("\t[Level]\n  Fog=\t1,  >  \n   2\n"));

		Assert.AreSequenceEqual(["\t[Level]", "  Fog=\t1,  >  "], lines.Select(line => line.SourceText));
		Assert.AreSequenceEqual(["\t[Level]", "Fog=\t1,2"], lines.Select(line => line.Text));
	}

	[TestMethod]
	public void SourceText_StopsBeforeTheComment()
	{
		// A ';' starts the comment wherever it stands, and a '//' only in a line without one, as the normalization reads it.
		List<ScriptSourceLine> lines = ReadAll(UnitRun.ForScript("Fog= 1 ; a comment\nSnow= 1 // a comment\nName= a // b ; c\n"));
		Assert.AreSequenceEqual(["Fog= 1 ", "Snow= 1 ", "Name= a // b "], lines.Select(line => line.SourceText));
	}

	[TestMethod]
	public void MarkerInsideASemicolonComment_DoesNotContinueTheLine()
	{
		// VB6 ignores a '>' that stands after the line's ';' (Module1.bas:5155-5163).
		List<ScriptSourceLine> lines = ReadAll(UnitRun.ForScript("Fog= 1,2,3 ; a > b\nSnow= 1\n"));
		Assert.AreSequenceEqual(["Fog= 1,2,3 ; a > b", "Snow= 1"], lines.Select(line => line.Text));
	}

	[TestMethod]
	[DataRow("LoadCamera= 0,0,0,0,0,1>\n2,0\n", 1, SplitsAValueOrAWord)]
	[DataRow("Name= Test>\nLevel\n", 1, SplitsAValueOrAWord)]
	[DataRow("Snow= 1\nFog= 1,>\n2>\n3\n", 3, SplitsAValueOrAWord)]
	[DataRow(
		"Legend= Press,> to go on\nTest Level\n",
		1,
		"The text 'to go on' after a '>' continuation would be dropped; move it before the '>' or onto the next line"
	)]
	[DataRow(
		"Fog= 1,2,3 // fog -> red\nLightning= ENABLED\n",
		1,
		"A '>' inside a '//' comment continues the line and swallows the next one; start the comment with ';' or remove the '>'"
	)]
	[DataRow("Snow= 1\nFog= 1,2,>", 2, "A '>' continuation on the last line of the file has no line to join; remove the '>'")]
	public void ContinuationThatReadsWrong_FailsAtTheLineOfItsMarker(string script, int expectedLine, string expectedMessage)
	{
		// VB6 reads LoadCamera='s sixth value as 12 and the name as TestLevel, drops the text after a '>', joins the next line into a '//'
		// comment, because only a ';' stops a continuation (Module1.bas:5149-5167), and reads past the end of the file on the last line.
		AssertReadError(script, expectedLine, expectedMessage);
	}

	[TestMethod]
	public void TextAfterACtrlZ_Fails_AndACtrlZAtTheEnd_EndsTheFile()
	{
		// VB6's Line Input ends the file at a Ctrl-Z (Module1.bas:5150), so the text after it is dropped (PD-57).
		AssertReadError("Snow= 1\n\u001AFog= 1,2,3\n", 2, TextFileUtilities.TextAfterEndOfFileMarkError);
		Assert.AreSequenceEqual(["Snow= 1"], ReadAll(UnitRun.ForScript("Snow= 1\n\u001A")).Select(line => line.Text));
	}

	[TestMethod]
	public void Include_ReadsTheFileWhereTheDirectiveStands()
	{
		// VB6 opens an include at its #INCLUDE line, reads it to its end and goes on with the line after the directive (Module1.bas:5173-5207).
		UnitRun workspace = UnitRun.ForScript("[Level]\n#INCLUDE \"Rooms.txt\"\nSnow= 1\n").With("Rooms.txt", "Fog= 1,2,3\nRain= 1\n");

		List<ScriptSourceLine> lines = ReadAll(workspace);

		Assert.AreSequenceEqual(
			[("[Level]", 1), ("Fog= 1,2,3", 1), ("Rain= 1", 2), ("Snow= 1", 3)],
			lines.Select(line => (line.Text, line.Location.Line))
		);

		Assert.AreSequenceEqual(
			[workspace.ScriptFile, workspace.PathOf("Rooms.txt"), workspace.PathOf("Rooms.txt"), workspace.ScriptFile],
			lines.Select(line => line.Location.File)
		);
	}

	[TestMethod]
	public void LoadedFiles_ListEachFileAsItIsRead()
	{
		// VB6 reads a file once for each #INCLUDE that names it (Module1.bas:5173-5207).
		UnitRun workspace = UnitRun.ForScript("#INCLUDE \"Rooms.txt\"\n#INCLUDE \"Rooms.txt\"\n").With("Rooms.txt", "Fog= 1,2,3\n");
		(_, ScriptSourceLoader loader, _) = Read(workspace);

		Assert.AreSequenceEqual(
			[workspace.ScriptFile, workspace.PathOf("Rooms.txt"), workspace.PathOf("Rooms.txt")],
			loader.LoadedFiles.Select(file => file.FilePath)
		);

		Assert.AreSequenceEqual(["Fog= 1,2,3"], loader.LoadedFiles[1].Lines);
	}

	[TestMethod]
	[DataRow("#INCLUDE Rooms.txt", "Fog= 1,2,3")]
	[DataRow("#INCLUDE \"My Rooms.txt\"", "Fog= 4,5,6")]
	[DataRow("#INCLUDE\"Rooms.txt\"", "Fog= 1,2,3")]
	public void IncludeName_UnquotedOrQuotedWithASpace_IsRead(string directive, string expectedText)
	{
		// VB6 takes the name from the second space-separated piece, between quotes (Module1.bas:4999-5022), so these fail there; the port
		// reads the name each of them writes (PD-37). The two files differ, so the text shows which one was read.
		UnitRun workspace = UnitRun.ForScript(directive + "\n").With("Rooms.txt", "Fog= 1,2,3\n").With("My Rooms.txt", "Fog= 4,5,6\n");
		Assert.AreEqual(expectedText, ReadAll(workspace).Single().Text);
	}

	[TestMethod]
	[DataRow(
		"#INCLUDE \"Rooms.txt\" \"Doors.txt\"",
		"The #INCLUDE line goes on after the quoted name \"Rooms.txt\"; write one #INCLUDE line per file"
	)]
	[DataRow("#INCLUDE My Rooms.txt", "The #INCLUDE file name 'My Rooms.txt' holds a space; write the name in double quotes")]
	[DataRow("#INCLUDE", "Missing file name in #INCLUDE directive")]
	[DataRow("#INCLUDE \"Rooms.txt", UnclosedQuote)]
	[DataRow("#INCLUDE \"My Rooms.txt", UnclosedQuote)]
	[DataRow("#INCLUDE \"Rooms.txt ; note", UnclosedQuote)]
	[DataRow("#INCLUDE \"Rooms.txt ; was \"old.txt\"", UnclosedQuote)]
	[DataRow("#INCLUDE \"Rooms.txt ; the \"old file", UnclosedQuote)]
	[DataRow("#INCLUDE \"Rooms;1.txt\"", CommentInName)]
	[DataRow("#INCLUDE \"Rooms//1.txt\"  ", CommentInName)]
	[DataRow("#INCLUDE \"Rooms;1.txt\" ; note", CommentInName)]
	[DataRow("#INCLUDE \"Rooms;1.txt\" ; see \"old\"", CommentInName)]
	[DataRow("#INCLUDE \"Missing.txt\"", "The include file was not found: Missing.txt")]
	public void IncludeThatNamesNoReadableFile_Fails(string directive, string expectedError)
	{
		// VB6 includes the first quoted name and ignores the rest of the line (Module1.bas:5016-5018), and fails a missing name and a missing
		// file (:5009-5015, 5032-5038); the port fails the rest of the line too (PD-37).
		UnitRun workspace = UnitRun.ForScript(directive + "\n").With("Rooms.txt", "Fog= 1,2,3\n").With("Doors.txt", "Snow= 1\n");
		Assert.AreEqual(expectedError, IncludeError(workspace));
	}

	[TestMethod]
	public void IncludeInASubfolder_IsReadBesideTheIncludingFile()
	{
		// VB6 opens <engine>\script\ plus the name after its last '\' (Module1.bas:5031, SoloNome at MioModulo.bas:805-818); the port opens the
		// file beside the including file first (PD-16).
		UnitRun workspace = UnitRun.ForScript("#INCLUDE \"sub\\Rooms.txt\"\n").With(Path.Combine("sub", "Rooms.txt"), "Fog= 1,2,3\n");

		ScriptSourceLine line = ReadAll(workspace).Single();

		Assert.AreEqual((workspace.PathOf("sub", "Rooms.txt"), "Fog= 1,2,3"), (line.Location.File, line.Text));
	}

	[TestMethod]
	public void IncludeMissingFromItsSubfolder_OpensTheFileVb6Opens()
	{
		// SoloNome keeps the name after the last '\' or ':' (MioModulo.bas:805-818), so VB6 opens script\Rooms.txt (Module1.bas:5031) (PD-16).
		UnitRun workspace = UnitRun.ForScript("#INCLUDE \"sub\\Rooms.txt\"\n").With("Rooms.txt", "Snow= 1\n");
		Assert.AreEqual(workspace.PathOf("Rooms.txt"), ReadAll(workspace).Single().Location.File);
	}

	[TestMethod]
	public void IncludeWithAForwardSlash_KeepsItsFolderInVb6sName()
	{
		// SoloNome cuts at '\' and ':' only (MioModulo.bas:805-818), so VB6 looks for sub/Rooms.txt in the script folder (PD-16).
		UnitRun workspace = UnitRun.ForScript("#INCLUDE \"sub/Rooms.txt\"\n").With("Rooms.txt", "Snow= 1\n");
		Assert.AreEqual("The include file was not found: sub/Rooms.txt", IncludeError(workspace));
	}

	[TestMethod]
	public void NestedIncludeWithAForwardSlash_FallsBackToTheScriptFolderWithItsFolderKept()
	{
		// VB6 keeps the name after its last '\' or ':' (SoloNome, MioModulo.bas:805-818) and looks in script\ (Module1.bas:5031),
		// so "sub/b.txt" included from x\a.txt opens script\sub/b.txt (PD-16).
		UnitRun workspace = UnitRun
			.ForScript("#INCLUDE \"x\\a.txt\"\n")
			.With(Path.Combine("x", "a.txt"), "#INCLUDE \"sub/b.txt\"\n")
			.With(Path.Combine("sub", "b.txt"), "Snow= 1\n");

		Assert.AreEqual(workspace.PathOf("sub", "b.txt"), ReadAll(workspace).Single().Location.File);
	}

	[TestMethod]
	public void NestedIncludeWithAForwardSlash_OpensTheFileBesideTheIncludingFile()
	{
		// VB6 opens script\sub/b.txt for "sub/b.txt" included from x\a.txt (SoloNome, MioModulo.bas:805-818; Module1.bas:5031) and fails
		// without that file (:5032-5037); the port opens script\x\sub\b.txt, beside the file that names it (PD-16).
		UnitRun workspace = UnitRun
			.ForScript("#INCLUDE \"x\\a.txt\"\n")
			.With(Path.Combine("x", "a.txt"), "#INCLUDE \"sub/b.txt\"\n")
			.With(Path.Combine("x", "sub", "b.txt"), "Snow= 1\n");

		Assert.AreEqual(workspace.PathOf("x", "sub", "b.txt"), ReadAll(workspace).Single().Location.File);
	}

	[TestMethod]
	public void IncludeFoundInBothFolders_FailsWhenTheFilesDiffer()
	{
		// Either file could be the one the author means: VB6 opens script\Rooms.txt (Module1.bas:5031), and the port the file beside the
		// including one (PD-16).
		UnitRun same = UnitRun
			.ForScript("#INCLUDE \"sub\\Rooms.txt\"\n")
			.With(Path.Combine("sub", "Rooms.txt"), "Snow= 1\n")
			.With("Rooms.txt", "Snow= 1\n");

		UnitRun different = UnitRun
			.ForScript("#INCLUDE \"sub\\Rooms.txt\"\n")
			.With(Path.Combine("sub", "Rooms.txt"), "Snow= 1\n")
			.With("Rooms.txt", "Snow= 2\n");

		Assert.AreEqual(same.PathOf("sub", "Rooms.txt"), ReadAll(same).Single().Location.File);

		Assert.AreEqual(
			$"The #INCLUDE file 'sub\\Rooms.txt' exists as {different.PathOf("sub", "Rooms.txt")} and as {different.PathOf("Rooms.txt")}, and the two differ; delete one of them or make them the same",
			IncludeError(different)
		);
	}

	[TestMethod]
	public void TwentyNestedIncludes_AreRead_AndTheNextOneFails()
	{
		// VB6's include stack holds 21 frames, Script.txt and 20 includes (VetInclude(20), Mod_ScriptDat.bas:131), and the next include
		// overflowed it with a runtime error.
		var allowed = UnitRun.ForScript("#INCLUDE \"Level1.txt\"\n");
		var tooDeep = UnitRun.ForScript("#INCLUDE \"Level1.txt\"\n");

		for (int depth = 1; depth <= 21; depth++)
		{
			string next = depth < 20 ? $"#INCLUDE \"Level{depth + 1}.txt\"\n" : "Snow= 1\n";

			allowed.With($"Level{depth}.txt", next);
			tooDeep.With($"Level{depth}.txt", depth < 21 ? $"#INCLUDE \"Level{depth + 1}.txt\"\n" : "Snow= 1\n");
		}

		Assert.AreEqual(allowed.PathOf("Level20.txt"), ReadAll(allowed).Single().Location.File);
		Assert.AreEqual("Too many nested #INCLUDE files, past the limit of 20; check for an include cycle", IncludeError(tooDeep));
	}

	/// <summary>
	/// Builds a script that includes a file <paramref name="outer"/> times, which includes a second
	/// one <paramref name="inner"/> times.
	/// </summary>
	private static UnitRun FanOutOfIncludes(int outer, int inner)
	{
		var workspace = UnitRun.ForScript(string.Concat(Enumerable.Repeat("#INCLUDE \"Outer.txt\"\n", outer)));

		workspace.With("Outer.txt", string.Concat(Enumerable.Repeat("#INCLUDE \"Inner.txt\"\n", inner)));
		workspace.With("Inner.txt", "Snow= 1\n");

		return workspace;
	}

	[TestMethod]
	public void TenThousandIncludeLoads_AreRead()
	{
		// VB6 has no count limit (PD-94). 100 loads of Outer.txt and 99 of Inner.txt for each make 10000 loads.
		Assert.HasCount(100 * 99, ReadAll(FanOutOfIncludes(outer: 100, inner: 99)));
	}

	[TestMethod]
	public void MoreThanTenThousandIncludeLoads_FailNamingTheLimit()
	{
		// 100 loads of Outer.txt and 100 of Inner.txt for each make 10100 loads, so the 10001st include fails.
		(_, ScriptSourceLoader loader, string? includeError) = Read(FanOutOfIncludes(outer: 100, inner: 100));

		Assert.IsNull(loader.Error, loader.Error?.Message);
		Assert.AreEqual("Too many #INCLUDE files, past the limit of 10000 loads in one run; include fewer files", includeError);
	}
}
