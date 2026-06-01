using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Tests.Model;

[TestClass]
[TestCategory("Fast")]
public sealed class NgArgumentValueTests
{
	[TestMethod]
	public void ForArray_CopiesTheElements_SoALaterChangeToTheSourceListIsNotSeen()
	{
		var source = new List<NgArgumentValue> { NgArgumentValue.ForInt32(NgArgumentType.Word, 1), NgArgumentValue.ForInt32(NgArgumentType.Word, 2) };

		var array = NgArgumentValue.ForArray(NgArgumentType.Array, source);
		source[0] = NgArgumentValue.ForInt32(NgArgumentType.Word, 99);
		source.Add(NgArgumentValue.ForInt32(NgArgumentType.Word, 3));

		IReadOnlyList<NgArgumentValue>? elements = array.ArrayValue;

		Assert.IsNotNull(elements);
		Assert.AreSequenceEqual([1, 2], elements.Select(element => element.Int32Value));
		Assert.IsNotInstanceOfType<List<NgArgumentValue>>(elements, "The elements must not be a list a caller can cast back and change.");
	}

	[TestMethod]
	public void Accessors_ReadTheValueOfTheirOwnType_AndRejectAnother()
	{
		var number = NgArgumentValue.ForInt32(NgArgumentType.Long, -1);
		var text = NgArgumentValue.ForString("*");
		var flag = NgArgumentValue.ForBool(true);

		Assert.AreEqual(-1, number.Int32Value);
		Assert.IsTrue(number.IsDiscard);
		Assert.IsNull(number.ArrayValue);
		Assert.AreEqual("*", text.StringValue);
		Assert.IsTrue(text.IsDiscard);
		Assert.IsTrue(flag.BoolValue);
		Assert.IsFalse(flag.IsDiscard);

		Assert.ThrowsExactly<InvalidOperationException>(() => number.StringValue);
		Assert.ThrowsExactly<InvalidOperationException>(() => text.Int32Value);
		Assert.ThrowsExactly<InvalidOperationException>(() => flag.Int32Value);
		Assert.ThrowsExactly<InvalidOperationException>(() => number.BoolValue);
	}
}
