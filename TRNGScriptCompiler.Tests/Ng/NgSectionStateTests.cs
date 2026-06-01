using TRNGScriptCompiler.Ng;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
[TestCategory("Fast")]
public sealed class NgSectionStateTests
{
	[TestMethod]
	public void Occurrences_StopAtTheLimit_UnlessItIsMinusOne()
	{
		// VB6 counts the lines of a command without regard to case and fails the one past MaxPresenze, unless that is -1 (Module1.bas:8480-8497).
		var state = new NgSectionState();

		Assert.IsTrue(state.TryIncrementOccurrence("Damage=", 2));
		Assert.IsTrue(state.TryIncrementOccurrence("damage=", 2));
		Assert.IsFalse(state.TryIncrementOccurrence("Damage=", 2));
		Assert.IsTrue(Enumerable.Range(0, 1000).All(_ => state.TryIncrementOccurrence("Customize=", -1)));
	}

	[TestMethod]
	public void DuplicateKeys_AreKeptPerCommand()
	{
		// VB6 keeps the keys of each command in its own list (BaseDuplicati.VetControlla, Module1.bas:6885-6931).
		var state = new NgSectionState();

		Assert.IsTrue(state.TryRegisterDuplicateKey("ColorRGB=", "1|0"));
		Assert.IsTrue(state.TryRegisterDuplicateKey("Equipment=", "1|0"));
		Assert.IsFalse(state.TryRegisterDuplicateKey("colorrgb=", "1|0"));
		Assert.IsTrue(state.TryRegisterDuplicateKey("ColorRGB=", "2|0"));
	}

	[TestMethod]
	public void Reset_ForgetsTheCountsAndTheKeys()
	{
		// VB6 empties the counts and the repeat lists before each level section (AzzeraPresenze and AzzeraDuplicati, Module1.bas:8421-8423).
		var state = new NgSectionState();
		state.TryIncrementOccurrence("Snow=", 1);
		state.TryRegisterDuplicateKey("Snow=", "0|0");

		state.Reset();

		Assert.IsTrue(state.TryIncrementOccurrence("Snow=", 1));
		Assert.IsTrue(state.TryRegisterDuplicateKey("Snow=", "0|0"));
	}
}
