using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
[TestCategory("Fast")]
public sealed class ExtensionsSectionBuilderTests
{
	private static (ExtensionsSectionBuilder Builder, UnitRun Unit) CreateBuilder()
	{
		var unit = new UnitRun();
		return (new ExtensionsSectionBuilder(unit.Run.Diagnostics, "[PCExtensions]"), unit);
	}

	[TestMethod]
	public void Extensions_FillTheirSlotsInVb6sOrder_AndCountEachSlotOnce()
	{
		// VB6's slots are Level=, Fmv= and Cut= (Module1.bas:4525), matched without regard to case, and it counts a slot the first time a line
		// fills it (:5545-5553).
		(ExtensionsSectionBuilder builder, UnitRun unit) = CreateBuilder();

		Assert.IsTrue(builder.TryAdd(unit.CommandLine(2, "Cut= TR4")));
		Assert.IsTrue(builder.TryAdd(unit.CommandLine(3, "fmv= BIK")));
		Assert.IsTrue(builder.TryAdd(unit.CommandLine(4, "FMV= BIK")));

		Assert.AreSequenceEqual([null, "BIK", "TR4"], builder.Extensions);
		Assert.AreSequenceEqual([(int?)null, 4, 2], builder.Locations.Select(location => location.Line), "Each slot points at its last line.");

		Assert.AreEqual(2, builder.FilledCount);
		DiagnosticAssert.HasWarning(unit.Diagnostics, 4, "'FMV=' repeats line 3 with the same value; remove one of the two lines");
	}

	[TestMethod]
	public void ExtensionGivenTwoValues_Fails()
	{
		// VB6 warns and keeps the last extension (Module1.bas:5546-5556); the port fails, because the script does not say which it means (PD-31).
		(ExtensionsSectionBuilder builder, UnitRun unit) = CreateBuilder();

		Assert.IsTrue(builder.TryAdd(unit.CommandLine(2, "Level= TR4")));
		Assert.IsFalse(builder.TryAdd(unit.CommandLine(3, "Level= PSX")));

		Assert.AreEqual("TR4", builder.Extensions[0]);

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			3,
			"'Level=' gives a value different from line 2, and the setting holds one value; keep one of the two lines"
		);
	}

	[TestMethod]
	[DataRow("Sound= WAV", "Unknown command for [PCExtensions] section: Sound=; correct its name or remove the line")]
	[DataRow("Foo= .a, .b", "Unknown command for [PCExtensions] section: Foo=; correct its name or remove the line")]
	[DataRow("Level= TR4,PSX", "Wrong number of arguments for 'Level=': expected 1, got 2")]
	public void InvalidExtensionLine_Fails(string line, string expectedError)
	{
		// VB6 takes the three slot commands alone, with one argument each (Module1.bas:5527-5543).
		(ExtensionsSectionBuilder builder, UnitRun unit) = CreateBuilder();

		Assert.IsFalse(builder.TryAdd(unit.CommandLine(2, line)));
		DiagnosticAssert.SingleError(unit.Diagnostics, 2, expectedError);
	}
}
