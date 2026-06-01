namespace TRNGScriptCompiler.Tests.Support;

/// <summary>
/// Guards the stock <c>Objects.h</c> of <see cref="TestFixtures.StockObjectsHPath"/>, which each corpus workspace holds
/// a copy of, so a test that compiles on the stock file compiles on what the corpus compiled on.
/// </summary>
[TestClass]
public sealed class StockObjectsHGuardTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void EveryCorpusObjectsH_IsTheStockFile()
	{
		byte[] stock = File.ReadAllBytes(TestFixtures.StockObjectsHPath);

		string[] copies = Directory.GetFiles(TestFixtures.CorpusDirectory, "Objects.h", SearchOption.AllDirectories);

		Assert.IsNotEmpty(copies, "The corpus holds a copy of Objects.h in each workspace.");

		string[] different = [.. copies.Where(copy => !File.ReadAllBytes(copy).AsSpan().SequenceEqual(stock)).Select(Path.GetFullPath)];

		Assert.IsEmpty(different, "These corpus copies of Objects.h differ from Fixtures/Stock/Objects.h.");
	}
}
