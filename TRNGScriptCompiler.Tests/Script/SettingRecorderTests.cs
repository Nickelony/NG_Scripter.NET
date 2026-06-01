using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
[TestCategory("Fast")]
public sealed class SettingRecorderTests
{
	[TestMethod]
	public void SecondLine_WarnsOnTheSameValue_AndFailsOnAnother()
	{
		// VB6 keeps the last value of a number or an extension and only ever sets a flag (Module1.bas:5402-5430, 5545-5556), so a second
		// value is lost without a word; the port fails a different one and warns on an identical one (PD-31).
		var unit = new UnitRun();
		var recorder = new SettingRecorder(unit.Run.Diagnostics);

		Assert.IsTrue(recorder.TryRecord("Security", "16", unit.CommandLine(2, "Security= $10")));
		Assert.IsTrue(recorder.TryRecord("SECURITY", "16", unit.CommandLine(3, "Security= 16")));
		Assert.IsFalse(recorder.TryRecord("Security", "17", unit.CommandLine(4, "Security= 17")));
		Assert.IsTrue(recorder.TryRecord("InputTimeOut", "17", unit.CommandLine(5, "InputTimeOut= 17")));

		DiagnosticAssert.HasWarning(unit.Diagnostics, 3, "'Security=' repeats line 2 with the same value; remove one of the two lines");

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			4,
			"'Security=' gives a value different from line 2, and the setting holds one value; keep one of the two lines"
		);
	}

	[TestMethod]
	public void Values_CompareWithRegardToCase()
	{
		// An extension is written as the script gives it (Module1.bas:5556, 6700), so TR4 and tr4 are two values.
		var unit = new UnitRun();
		var recorder = new SettingRecorder(unit.Run.Diagnostics);

		Assert.IsTrue(recorder.TryRecord("Level=", "TR4", unit.CommandLine(2, "Level= TR4")));
		Assert.IsFalse(recorder.TryRecord("Level=", "tr4", unit.CommandLine(3, "Level= tr4")));

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			3,
			"'Level=' gives a value different from line 2, and the setting holds one value; keep one of the two lines"
		);
	}
}
