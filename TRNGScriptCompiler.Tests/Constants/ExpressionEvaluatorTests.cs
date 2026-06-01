using TRNGScriptCompiler.Constants;

namespace TRNGScriptCompiler.Tests.Constants;

[TestClass]
[TestCategory("Fast")]
public sealed class ExpressionEvaluatorTests
{
	/// <summary>A resolver that knows two constants and returns a plugin ID for <c>PLUGINWORD</c>.</summary>
	private static ConstantResolutionResult Resolve(string name, string? file)
	{
		return name.ToUpperInvariant() switch
		{
			"ALPHA" => new ConstantResolutionResult(Success: true, PluginId: 0, Value: 10, Kind: ConstantKind.Constant),
			"BETA" => new ConstantResolutionResult(Success: true, PluginId: 0, Value: 3, Kind: ConstantKind.Constant),
			"FORWARD" => new ConstantResolutionResult(Success: true, PluginId: 0, Value: 7, Kind: ConstantKind.Constant), // resolvable even when defined later in the file
			"PLUGINWORD" => new ConstantResolutionResult(Success: true, PluginId: 42, Value: 500, Kind: ConstantKind.PluginConstant),
			_ => default,
		};
	}

	private static ExpressionEvaluator CreateEvaluator() => new(Resolve);

	[TestMethod]
	[DataRow("2+3", 5)]
	[DataRow("10-3-2", 5)] // subtraction is left-to-right within each + group
	[DataRow("2+3-1+10", 14)]
	[DataRow("ALPHA+BETA", 13)]
	[DataRow("ALPHA+BETA-5", 8)]
	[DataRow("$10+1", 17)] // hex addend
	[DataRow("#10", 16)]
	[DataRow("$FFFFFFFF", -1)] // VB6 reads $ as Val("&h...&"), a 32-bit Long (Module1.bas:4870-4884)
	[DataRow("&H7400", 29696)]
	[DataRow("&O17", 15)]
	[DataRow(" alpha + 1 ", 11)] // case-insensitive and whitespace-tolerant
	[DataRow("FORWARD+0", 7)] // forward reference resolves through the callback
	[DataRow("-5", -5)] // a leading sign
	[DataRow("+5", 5)]
	[DataRow("- 5+ALPHA", 5)]
	public void Evaluate_ComputesSupportedExpressions(string expression, int expected)
	{
		NumericEvaluation evaluation = CreateEvaluator().Evaluate(expression, file: null);

		Assert.IsTrue(evaluation.Success, $"'{expression}' should evaluate: {evaluation.Error}");
		Assert.AreEqual(expected, evaluation.Value);
	}

	[TestMethod]
	public void Evaluate_TracksPluginIdOfLastResolvedConstant()
	{
		NumericEvaluation evaluation = CreateEvaluator().Evaluate("PLUGINWORD+1", file: null);
		Assert.AreEqual((true, 501, 42), (evaluation.Success, evaluation.Value, evaluation.PluginId));
	}

	[TestMethod]
	public void Evaluate_PlainNumbers_ReportNoPlugin()
	{
		NumericEvaluation evaluation = CreateEvaluator().Evaluate("5+5", file: null);
		Assert.AreEqual((true, 10, 0), (evaluation.Success, evaluation.Value, evaluation.PluginId));
	}

	[TestMethod]
	[DataRow("5+-3", "5+-3 has an operator with no value after it; write 5-3")]
	[DataRow("5--3", "5--3 has an operator with no value after it; write 5+3")]
	[DataRow("5+", "5+ has an operator with no value after it; write 5")]
	[DataRow("5-", "5- has an operator with no value after it; write 5")]
	[DataRow("-", "- has an operator with no value after it; write a number")]
	[DataRow("+", "+ has an operator with no value after it; write a number")]
	[DataRow("--3", "--3 has an operator with no value after it; write 3")]
	public void Evaluate_OperatorWithoutAValue_FailsNamingTheFix(string expression, string expectedError)
	{
		// VB6 splits at + and then at -, reuses the previous value for an empty piece after a minus and adds nothing for one after a
		// plus (Module1.bas:4857-4911), so it reads 5+-3 as 7, 5--3 as -3, 5+ as 5, 5- as 0 and a lone sign as 0 (PD-51).
		NumericEvaluation evaluation = CreateEvaluator().Evaluate(expression, file: null);
		Assert.AreEqual((false, 0, expectedError), (evaluation.Success, evaluation.Value, evaluation.Error));
	}

	[TestMethod]
	[DataRow("&HFFFF", "VB reads &HFFFF as -1; write $FFFF for 65535, or -1 for the value VB reads")]
	[DataRow("&H8000", "VB reads &H8000 as -32768; write $8000 for 32768, or -32768 for the value VB reads")]
	[DataRow("&H80000000", "VB reads &H80000000 as -2147483648; write $80000000 for 2147483648, or -2147483648 for the value VB reads")]
	[DataRow("&O177777", "VB reads &O177777 as -1; write $FFFF for 65535, or -1 for the value VB reads")]
	[DataRow("1.5", "1.5 is not a whole number; write it without a fraction, a separator or an exponent")]
	[DataRow("1e1", "1e1 is not a whole number; write it without a fraction, a separator or an exponent")]
	[DataRow("0x10", "0x10 is not a number; write $10 for a hexadecimal value")]
	[DataRow("$ 12", "$ 12 is not a hexadecimal number; write 1 to 8 hexadecimal digits after $")]
	[DataRow("$100000000", "$100000000 does not fit 32 bits")]
	[DataRow("12abc", "12abc is not a number; a decimal number holds digits only")]
	[DataRow("3000000000", "3000000000 does not fit 32 bits")]
	[DataRow("2147483647+1", "2147483647+1 overflows 32 bits")]
	[DataRow("(5)", "(5) is neither a number nor a known name")]
	[DataRow("NOPE", "NOPE is neither a number nor a known name")]
	[DataRow("5+NOPE", "NOPE is neither a number nor a known name")]
	[DataRow("", "the value is empty")]
	[DataRow("   ", "the value is empty")]
	public void Evaluate_NumberThatReadsWrong_FailsWithAReason(string expression, string expectedError)
	{
		// VB's Val reads &H8000-&HFFFF as a negative Integer and 1.5 through the Windows locale, VB6's hexadecimal check rejects
		// "$ 12" and IsNumeric rejects 0x10 (Module1.bas:4870-4900, MioModulo.bas:4986-5011), and a sum past a Long raises error 6.
		NumericEvaluation evaluation = CreateEvaluator().Evaluate(expression, file: null);
		Assert.AreEqual((false, 0, expectedError), (evaluation.Success, evaluation.Value, evaluation.Error));
	}

	[TestMethod]
	public void Evaluate_RefusedTerm_ReportsTheTermAndItsTable()
	{
		NumericEvaluation evaluation = CreateEvaluator().Evaluate("1+alpha", file: null, (_, kind) => kind is null);

		Assert.IsFalse(evaluation.Success);
		Assert.AreEqual<RejectedTerm?>(new RejectedTerm("ALPHA", ConstantKind.Constant), evaluation.Rejection);
	}
}
