using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Tests.Kernel.Text;

[TestClass]
[TestCategory("Fast")]
public sealed class EnabledStateTests
{
	[TestMethod]
	[DataRow("ENABLED", true)]
	[DataRow("enabled", true)]
	[DataRow("Disabled", false)]
	[DataRow("DISABLED", false)]
	public void TryParse_ReadsEitherWordWithoutRegardToCase(string value, bool expected)
	{
		// VB6 compares the argument with ControllaEnabled without regard to case.
		Assert.IsTrue(EnabledState.TryParse(value, out bool enabled));
		Assert.AreEqual(expected, enabled);
	}

	[TestMethod]
	[DataRow("ON")]
	[DataRow("")]
	[DataRow("1")]
	[DataRow("ENABLED1")]
	public void TryParse_FailsAnyOtherValue(string value)
	{
		Assert.IsFalse(EnabledState.TryParse(value, out bool enabled));
		Assert.IsFalse(enabled);
	}

	[TestMethod]
	public void InvalidValueError_NamesTheArgumentTheCommandAndTheValue()
	{
		Assert.AreEqual(
			"Argument 2 of Train= is invalid: ON is neither ENABLED nor DISABLED",
			EnabledState.InvalidValueError(argumentNumber: 2, command: "Train=", value: "ON")
		);
	}
}
