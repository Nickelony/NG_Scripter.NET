using TRNGScriptCompiler.Constants;

namespace TRNGScriptCompiler.Tests.Constants;

[TestClass]
[TestCategory("Fast")]
public sealed class FieldRangeTests
{
	[TestMethod]
	[DataRow(-128, 1, true)]
	[DataRow(255, 1, true)]
	[DataRow(-129, 1, false)]
	[DataRow(256, 1, false)]
	[DataRow(-32768, 2, true)]
	[DataRow(65535, 2, true)]
	[DataRow(65536, 2, false)]
	public void Fits_TakesTheSignedAndTheUnsignedReading(int value, int widthInBytes, bool fits) =>
		Assert.AreEqual(fits, FieldRange.Fits(value, widthInBytes, minimum: out _, maximum: out _));

	[TestMethod]
	public void DescribeOutside_NamesTheValueTheRangeAndTheHolder()
	{
		string message = FieldRange.DescribeOutside(value: 70000, minimum: -32768, maximum: 65535, holder: "its word");
		Assert.AreEqual("70000 lies outside -32768 to 65535, the values its word holds", message);
	}
}
