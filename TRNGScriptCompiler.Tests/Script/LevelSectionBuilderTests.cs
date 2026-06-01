using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
public sealed class LevelSectionBuilderTests
{
	private static (LevelSectionBuilder Builder, UnitRun Unit) CreateBuilder(
		ScriptSectionType type = ScriptSectionType.Level,
		IReadOnlySet<string>? ngCommandNames = null
	)
	{
		var unit = new UnitRun();

		return (
			new LevelSectionBuilder(
				diagnostics: unit.Run.Diagnostics,
				sectionType: type,
				ngCommandNames: ngCommandNames ?? UnitRun.NgCommandNames,
				headerLocation: unit.At(3)
			),
			unit
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ClassicLines_AreKept_WithTheNameAsTheRestOfItsLine()
	{
		// VB6 keeps a classic line for the section compile (Module1.bas:5375-5383) and reads Name= as the rest of its line, commas included
		// (:5995-6002); the level path is the first argument of Level= (:5809).
		(LevelSectionBuilder builder, UnitRun unit) = CreateBuilder();

		Assert.IsTrue(builder.TryAdd(unit.CommandLine(4, "Name= Coastal Ruins, Part 2")));
		Assert.IsTrue(builder.TryAdd(unit.CommandLine(5, @"Level= data\coastal,104")));
		Assert.IsTrue(builder.TryAdd(unit.CommandLine(6, "Fog= 1,2,3")));

		ParsedSection section = builder.Build();

		Assert.AreEqual(
			(ScriptSectionType.Level, "Coastal Ruins, Part 2", @"data\coastal", 3),
			(section.SectionType, section.LevelName, section.LevelPath, section.HeaderLocation.Line)
		);

		Assert.AreSequenceEqual(
			[("Name= Coastal Ruins, Part 2", 4), (@"Level= data\coastal,104", 5), ("Fog= 1,2,3", 6)],
			section.ClassicLines.Select(line => (line.Text, line.Location.Line))
		);

		Assert.IsEmpty(section.NgLines);
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void NgLines_AreCollectedAsWritten()
	{
		// VB6 keeps an NG line of a [Level] or [Title] for the NG header, with its line and file (Module1.bas:5320-5372).
		(LevelSectionBuilder builder, UnitRun unit) = CreateBuilder(ScriptSectionType.Title);

		Assert.IsTrue(builder.TryAdd(unit.CommandLine(4, "snow= 1")));

		ScriptCommandLine line = builder.Build().NgLines.Single().CommandLine;

		Assert.AreEqual(("snow=", "snow= 1", 4, unit.ScriptFile), (line.Command, line.Text, line.Location.Line, line.Location.File));
		Assert.AreSequenceEqual(["1"], line.Arguments);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ClassicCommand_WinsOverAnNgCommandOfTheSameName()
	{
		// VB6 looks a line up in its classic table first and in the NG table only when the classic one lacks it (Module1.bas:5306-5322).
		(LevelSectionBuilder builder, UnitRun unit) = CreateBuilder(ngCommandNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Fog=" });

		Assert.IsTrue(builder.TryAdd(unit.CommandLine(4, "Fog= 1,2,3")));

		ParsedSection section = builder.Build();

		Assert.AreEqual((1, 0), (section.ClassicLines.Count, section.NgLines.Count));
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow((int)ScriptSectionType.Level, "Unknown command for [Level] section: FooBar=; correct its name or remove the line")]
	[DataRow((int)ScriptSectionType.Title, "Unknown command for [Title] section: FooBar=; correct its name or remove the line")]
	public void UnknownCommand_FailsAtItsName(int type, string expectedError)
	{
		// VB6 fails a command that neither table holds where it stands (Module1.bas:5324-5332).
		(LevelSectionBuilder builder, UnitRun unit) = CreateBuilder((ScriptSectionType)type);

		Assert.IsFalse(builder.TryAdd(unit.CommandLine(4, "FooBar= 1")));

		CompilerDiagnostic error = DiagnosticAssert.SingleError(
			diagnostics: unit.Diagnostics,
			line: 4,
			message: expectedError,
			file: unit.ScriptFile
		);

		Assert.AreEqual((0, "FooBar=".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void PluginDefine_StandsAmongTheNgLines()
	{
		// VB6 keeps a #define @plugin line among the NG commands of its section, so it applies where it stands (Module1.bas:5283-5289,
		// 5360-5372).
		(LevelSectionBuilder builder, UnitRun unit) = CreateBuilder();

		Assert.IsTrue(builder.TryAdd(unit.CommandLine(4, "Snow= 1")));
		builder.AddPluginDefine("#DEFINE @Plugin_One 1", sourceText: "#DEFINE @Plugin_One 1", unit.At(5));
		Assert.IsTrue(builder.TryAdd(unit.CommandLine(6, "Rain= 1")));

		Assert.AreSequenceEqual(
			[(false, 4), (true, 5), (false, 6)],
			builder.Build().NgLines.Select(line => (line.IsPluginDefine, line.CommandLine.Location.Line))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void UnknownLevelCommand_Fails()
	{
		const string script = TestScripts.LanguageAndLevel + "FooBar= 1\n";

		using var workspace = TestWorkspace.CreateCompilable(script, TestWorkspace.CompilableLanguageContent);
		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "VB6 treats unknown level commands as fatal errors.");
		DiagnosticAssert.SingleError(result.Diagnostics, 7, "Unknown command for [Level] section: FooBar=; correct its name or remove the line");
	}
}
