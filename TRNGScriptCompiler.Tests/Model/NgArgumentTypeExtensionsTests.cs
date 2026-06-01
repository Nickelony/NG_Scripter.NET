using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Tests.Model;

[TestClass]
[TestCategory("Fast")]
public sealed class NgArgumentTypeExtensionsTests
{
	[TestMethod]
	public void IsArray_IsTrueForTheArrayTypesOnly()
	{
		NgArgumentType[] arrays = [NgArgumentType.ArrayByte, NgArgumentType.Array, NgArgumentType.ArrayNybble, NgArgumentType.ArrayLong];
		Assert.AreSequenceEqual(arrays, Enum.GetValues<NgArgumentType>().Where(type => type.IsArray()));
	}
}
