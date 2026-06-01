namespace TRNGScriptCompiler.Tests.Kernel.Runtime;

[TestClass]
public sealed class MainLanguageSelectorTests
{
	[TestMethod]
	[TestCategory("Fast")]
	public void MainLanguageSelector_Equality_ComparesKindAndTextWithoutRegardToCase()
	{
		var english = MainLanguageSelector.FromText("English.txt");

		Assert.AreEqual(english, MainLanguageSelector.FromText(" english.TXT "));
		Assert.AreEqual(english!.GetHashCode(), MainLanguageSelector.FromText("ENGLISH.txt")!.GetHashCode());
		Assert.IsTrue(english == MainLanguageSelector.FromText("english.txt"));
		Assert.IsFalse(english != MainLanguageSelector.FromText("english.txt"));
		Assert.AreNotEqual(english, MainLanguageSelector.FromText("French.txt"));
		Assert.IsFalse(english.Equals(null));
		Assert.IsFalse(english == null);
		Assert.IsTrue(english != null);

		Assert.AreEqual(MainLanguageSelector.FromIndex(2), MainLanguageSelector.FromText(" 2 "));
		Assert.AreEqual(MainLanguageSelector.FromIndex(2).GetHashCode(), MainLanguageSelector.FromText("2")!.GetHashCode());
		Assert.AreNotEqual(MainLanguageSelector.FromIndex(2), MainLanguageSelector.FromIndex(3));
		Assert.AreNotEqual(MainLanguageSelector.FromIndex(2), MainLanguageSelector.FromText("English.txt"));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void MainLanguageSelector_FromText_ParsesIndexNameAndBlank()
	{
		var byIndex = MainLanguageSelector.FromText(" 2 ");

		Assert.IsNotNull(byIndex);
		Assert.IsTrue(byIndex.IsIndex, "Numeric text must parse as an index selector.");
		Assert.AreEqual(2, byIndex.Index, "The parsed index must be the numeric value.");

		var byName = MainLanguageSelector.FromText(" English.txt ");

		Assert.IsNotNull(byName);
		Assert.IsFalse(byName.IsIndex, "Non-numeric text must stay a name selector.");
		Assert.AreEqual("English.txt", byName.Text, "Selector text must be trimmed for diagnostics and matching.");

		Assert.IsNull(MainLanguageSelector.FromText("   "), "Blank text must map to no selector so the pipeline applies its default.");
		Assert.IsNull(MainLanguageSelector.FromText(null), "Missing text must map to no selector so the pipeline applies its default.");
	}
}
