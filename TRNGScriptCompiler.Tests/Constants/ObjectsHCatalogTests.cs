using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Constants;

[TestClass]
[TestCategory("IO")]
public sealed class ObjectsHCatalogTests
{
	private const string ObjectsH = """
		enum object_types
		{
		    SLOT_FIRST,
		    SLOT_SECOND,   // inline comment
		    // commented out entirely
		    SLOT_THIRD,
		};

		enum static_types
		{
		    STATIC_ONLY
		};
		""";

	private static (ObjectsHCatalog Catalog, CompilerLog Logger) Load(string content) => Load(content, out _, out _);

	private static (ObjectsHCatalog Catalog, CompilerLog Logger) Load(string content, out DiagnosticSink diagnostics, out string path)
	{
		var logger = new CompilerLog(conciseLogging: false);
		diagnostics = new DiagnosticSink(logger);
		var catalog = new ObjectsHCatalog(logger, diagnostics);

		using var workspace = TestWorkspace.CreateEmpty();
		path = workspace.PathOf("Objects.h");
		File.WriteAllText(path, content);

		catalog.Load(path);

		return (catalog, logger);
	}

	[TestMethod]
	public void DefineWithoutObjectsH_StillResolves()
	{
		// PD-52: without Objects.h VB6 stops looking after the constants file (Module1.bas:10131-10134), so MY_ID is unknown.
		using var defineWorkspace = TestWorkspace.CreateCompilable("#define MY_ID 5\n" + TestScripts.LanguageAndLevel + "Customize= MY_ID,1,2\n");
		using var numberWorkspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "Customize= 5,1,2\n");

		TestCompileResult defineResult = defineWorkspace.Compile();
		TestCompileResult numberResult = numberWorkspace.Compile();

		Assert.IsTrue(defineResult.Success, defineResult.Log);
		Assert.IsTrue(numberResult.Success, numberResult.Log);

		DiagnosticAssert.HasWarning(
			diagnostics: defineResult.Diagnostics,
			line: null,
			message: $"Objects.h was not found: {defineWorkspace.PathOf("Objects.h")}",
			file: defineWorkspace.PathOf("Objects.h")
		);

		Assert.AreSequenceEqual(numberWorkspace.ReadScriptDat(), defineWorkspace.ReadScriptDat());
	}

	[TestMethod]
	public void Load_UsesOrdinalPositions()
	{
		(ObjectsHCatalog? catalog, CompilerLog _) = Load(ObjectsH);

		Assert.IsTrue(catalog.TryGetSlotConstant("SLOT_FIRST", out int first));
		Assert.AreEqual(0, first);

		Assert.IsTrue(catalog.TryGetSlotConstant("SLOT_SECOND", out int second));
		Assert.AreEqual(1, second);

		Assert.IsTrue(catalog.TryGetSlotConstant("SLOT_THIRD", out int third));
		Assert.AreEqual(2, third);

		Assert.IsTrue(catalog.TryGetStaticConstant("STATIC_ONLY", out int staticValue));
		Assert.AreEqual(0, staticValue);
	}

	[TestMethod]
	public void Load_SplitsAnEnumBodyAtEveryComma()
	{
		// Module1.bas:9052-9068 ends an item at every comma, drops spaces, tabs and line breaks, and skips an empty item, so the
		// stock static_types, ten names to a line, resolve by position.
		(ObjectsHCatalog? catalog, CompilerLog _) = Load(
			"enum object_types {\n\tLARA,\n\tNUMBER_OBJECTS\n};\nenum static_types {\n\tPLANT0, PLANT1,\tPLANT2,, PLANT3,\n\tFURNITURE0\n};\n"
		);

		Assert.IsTrue(catalog.TryGetStaticConstant("PLANT1", out int plant1));
		Assert.AreEqual(1, plant1);

		Assert.IsTrue(catalog.TryGetStaticConstant("PLANT3", out int plant3));
		Assert.AreEqual(3, plant3);

		Assert.IsTrue(catalog.TryGetStaticConstant("FURNITURE0", out int furniture0));
		Assert.AreEqual(4, furniture0);
	}

	[TestMethod]
	public void Load_NoBreakSpaceAfterAnItem_ResolvesWithAndWithoutAComment()
	{
		// The port trims each line it reads, so a no-break space, which VB6's item loop does not remove, ends no name, with a comment
		// after the item or without one. The byte-order mark makes the file read as UTF-8, so the no-break space reaches the parser.
		(ObjectsHCatalog? catalog, CompilerLog _) = Load(
			"\U0000FEFFenum object_types {\n\tSLOT_A\U000000A0\n\t, SLOT_B\U000000A0 // note\n\t, SLOT_C\n};\n"
		);

		Assert.IsTrue(catalog.TryGetSlotConstant("SLOT_A", out int first));
		Assert.IsTrue(catalog.TryGetSlotConstant("SLOT_B", out int second));
		Assert.IsTrue(catalog.TryGetSlotConstant("SLOT_C", out int third));
		Assert.AreSequenceEqual(new[] { 0, 1, 2 }, new[] { first, second, third });
	}

	[TestMethod]
	public void StockStaticNames_CompileToTheirPositions()
	{
		// The stock Objects.h lists ten statics to a line, which Module1.bas:9052-9068 splits at the commas, so PLANT1 is static 1.
		byte[] byName = TestWorkspace.CompileScriptDatOnStockObjectsH(TestScripts.LanguageAndLevel + "StaticMIP= PLANT1,4,PLANT2,8,PLANT3\n");
		byte[] byNumber = TestWorkspace.CompileScriptDatOnStockObjectsH(TestScripts.LanguageAndLevel + "StaticMIP= 1,4,2,8,3\n");

		Assert.AreSequenceEqual(byNumber, byName);
	}

	[TestMethod]
	public void Load_ItemWithAnExplicitValue_KeepsItsPositionAndDoesNotResolve()
	{
		// Module1.bas:9052-9068 keeps "SLOT_FIRST=9" as the item text, so the name never matches a script argument (:10139-10140).
		(ObjectsHCatalog? catalog, CompilerLog _) = Load(
			"enum object_types\n{\n    SLOT_FIRST = 9,\n    SLOT_SECOND,\n};\n",
			out DiagnosticSink diagnostics,
			out string path
		);

		Assert.IsFalse(catalog.TryGetSlotConstant("SLOT_FIRST", out _));
		Assert.IsTrue(catalog.TryGetSlotConstant("SLOT_SECOND", out int second));
		Assert.AreEqual(1, second);

		DiagnosticAssert.HasWarning(
			diagnostics: diagnostics.Recorded,
			line: 3,
			message: "Objects.h item SLOT_FIRST=9 in enum object_types assigns a value, so SLOT_FIRST does not resolve, as in VB6; remove the assignment to use the name",
			file: path
		);
	}

	[TestMethod]
	public void Load_NameListedTwice_ResolvesToItsFirstPositionAndKeepsLaterPositions()
	{
		// Module1.bas:9052-9068 keeps both items, and the lookup stops at the first (:10139-10140).
		// The warning points at the second SLOT_A, on line 5; a comment line above the enum and an item that shares a line with the brace
		// keep the count.
		(ObjectsHCatalog? catalog, CompilerLog _) = Load(
			"// slots\r\nenum object_types\r\n{ SLOT_A,\r\n    SLOT_B, // the second\r\n    SLOT_A,\r\n    SLOT_C,\r\n};\r\n",
			out DiagnosticSink diagnostics,
			out string path
		);

		Assert.IsTrue(catalog.TryGetSlotConstant("SLOT_A", out int first));
		Assert.AreEqual(0, first);
		Assert.IsTrue(catalog.TryGetSlotConstant("SLOT_C", out int last));
		Assert.AreEqual(3, last);

		DiagnosticAssert.HasWarning(
			diagnostics: diagnostics.Recorded,
			line: 5,
			message: "Objects.h lists SLOT_A twice in enum object_types; it resolves to its first position, 0",
			file: path
		);
	}

	[TestMethod]
	public void Load_LookupsAreCaseInsensitive()
	{
		(ObjectsHCatalog? catalog, CompilerLog _) = Load(ObjectsH);

		Assert.IsTrue(catalog.TryGetSlotConstant("slot_first", out int value));
		Assert.AreEqual(0, value);

		Assert.IsFalse(catalog.TryGetSlotConstant("SLOT_MISSING", out int missing));
		Assert.AreEqual(0, missing);
	}

	[TestMethod]
	public void Load_MissingFile_Warns()
	{
		var logger = new CompilerLog(conciseLogging: false);
		var diagnostics = new DiagnosticSink(logger);
		var catalog = new ObjectsHCatalog(logger, diagnostics);

		using var workspace = TestWorkspace.CreateEmpty();

		catalog.Load(workspace.PathOf("Missing.h"));

		DiagnosticAssert.HasWarning(
			diagnostics: diagnostics.Recorded,
			line: null,
			message: $"Objects.h was not found: {workspace.PathOf("Missing.h")}",
			file: workspace.PathOf("Missing.h")
		);
	}

	[TestMethod]
	public void Load_MissingSlotEnum_WarnsButSucceeds()
	{
		(ObjectsHCatalog? catalog, CompilerLog _) = Load(
			"enum static_types\n{\n    STATIC_ONLY\n};\n",
			out DiagnosticSink diagnostics,
			out string path
		);

		DiagnosticAssert.HasWarning(
			diagnostics: diagnostics.Recorded,
			line: null,
			message: "Could not find enum object_types in Objects.h",
			file: path
		);

		Assert.IsTrue(catalog.TryGetStaticConstant("STATIC_ONLY", out _));
		Assert.IsFalse(catalog.TryGetSlotConstant("SLOT_FIRST", out _));
	}

	[TestMethod]
	public void Load_FindsTheSlotEnumByName()
	{
		// PD-52: VB6 takes the first enum in the file as the slot enum (Module1.bas:9036, 10138).
		(ObjectsHCatalog? catalog, CompilerLog _) = Load("enum floor_types {\n\tFLOOR_TYPE\n};\nenum object_types {\n\tLARA,\n\tPISTOLS_ANIM\n};\n");

		Assert.IsFalse(catalog.TryGetSlotConstant("FLOOR_TYPE", out _));
		Assert.IsTrue(catalog.TryGetSlotConstant("PISTOLS_ANIM", out int pistols));
		Assert.AreEqual(1, pistols);
	}

	[TestMethod]
	public void Load_SkipsCommentLines()
	{
		// PD-52: Module1.bas:9052-9068 joins a comment to the next name and ends an item at a comma inside it.
		(ObjectsHCatalog? catalog, CompilerLog _) = Load(
			"enum object_types\n{\n    // SLOT_COMMENTED = 1\n    SLOT_REAL, // a note, with a comma\n    SLOT_NEXT,\n};\n"
		);

		Assert.IsFalse(catalog.TryGetSlotConstant("SLOT_COMMENTED", out _));
		Assert.IsTrue(catalog.TryGetSlotConstant("SLOT_REAL", out int real));
		Assert.AreEqual(0, real);
		Assert.IsTrue(catalog.TryGetSlotConstant("SLOT_NEXT", out int next));
		Assert.AreEqual(1, next);
	}

	[TestMethod]
	public void ObjectsHThatAnotherProcessLocks_WarnsNamingTheFile()
	{
		using TestWorkspace workspace = TestWorkspace.CreateCompilable().WithObjectsH();
		string path = workspace.PathOf("Objects.h");

		TestCompileResult result = workspace.WithFileLocked("Objects.h", () => workspace.Compile());

		Assert.IsTrue(result.Success, "An unreadable Objects.h leaves its names unknown, as a missing one does: " + result.Log);

		// The message ends in the operating system's message, which the warning keeps in its exception.
		CompilerDiagnostic warning = result.Diagnostics.Single(diagnostic => diagnostic.Exception is not null);
		Assert.IsInstanceOfType<IOException>(warning.Exception);

		DiagnosticAssert.HasWarning(
			result.Diagnostics,
			line: null,
			$"Objects.h cannot be read, so its slot and static names are unknown: {warning.Exception.Reason()}"
		);

		Assert.AreEqual(path, warning.SourceFile, ignoreCase: true);
	}
}
