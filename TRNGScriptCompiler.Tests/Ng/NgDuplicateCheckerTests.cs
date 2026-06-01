using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
public sealed class NgDuplicateCheckerTests
{
	private const string Duplicate = "was already present in same [level/title/option] section";

	private static readonly LanguageData s_language = UnitRun.Language("Test Level");

	private sealed class Checker
	{
		private readonly NgDuplicateChecker _checker;
		private readonly NgSectionState _state = new();

		internal Checker()
		{
			Unit = new UnitRun();
			_checker = new NgDuplicateChecker(Unit.Run.Diagnostics, new NgCommandEncoder(Unit.Run.Diagnostics, s_language));
		}

		internal UnitRun Unit { get; }

		internal bool Register(int line, string command, params int[] values)
		{
			List<NgArgumentValue> arguments = [.. values.Select(value => NgArgumentValue.ForInt32(NgArgumentType.Word, value))];

			return _checker.TryRegister(
				sectionState: _state,
				definition: UnitRun.Stock(command),
				commandArgs: arguments,
				nameLocation: Unit.At(line)
			);
		}
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void SecondLineWithTheSameFirstArgument_IsADuplicate_IgnoreReadingAsZero()
	{
		// VB6 keys a repeat on the first argument and reads -1 as 0 (Module1.bas:6883, 7594-7605).
		var checker = new Checker();

		Assert.IsTrue(checker.Register(4, "ColorRGB=", -1, 1, 1, 1));
		Assert.IsTrue(checker.Register(5, "ColorRGB=", 1, 1, 1, 1));

		Assert.IsFalse(checker.Register(6, "ColorRGB=", 0, 2, 2, 2));

		DiagnosticAssert.SingleError(checker.Unit.Diagnostics, 6, $"Duplicated command [ColorRGB= 0, ...] {Duplicate}");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parameters_IsADuplicateOnItsFirstTwoArguments()
	{
		// VB6 keys Parameters= on its first two arguments (VetDoppi, Module1.bas:1246).
		var checker = new Checker();

		Assert.IsTrue(checker.Register(4, "Parameters=", 1, 2, 3));
		Assert.IsTrue(checker.Register(5, "Parameters=", 1, 3, 3));

		Assert.IsFalse(checker.Register(6, "Parameters=", 1, 2, 4));

		DiagnosticAssert.SingleError(checker.Unit.Diagnostics, 6, $"Duplicated command [Parameters= 1, 2, ...] {Duplicate}");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void AnimationSlot_IsADuplicateOnItsSlotAndAnimation()
	{
		// VB6 keys AnimationSlot= on its slot alone (Module1.bas:7594-7605), so a slot takes one animation; the port keys on the slot and the
		// animation index (PD-63).
		var checker = new Checker();

		Assert.IsTrue(checker.Register(4, "AnimationSlot=", 0, 1, 5, 0, 0, 0, 0, 0, 0));
		Assert.IsTrue(checker.Register(5, "AnimationSlot=", 0, 1, 6, 0, 0, 0, 0, 0, 0));

		Assert.IsFalse(checker.Register(6, "AnimationSlot=", 0, 2, 5, 0, 0, 0, 0, 0, 0));

		DiagnosticAssert.SingleError(checker.Unit.Diagnostics, 6, $"Duplicated command [AnimationSlot= 0, 2, 5, ...] {Duplicate}");
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("Animation=")]
	[DataRow("MirrorEffect=")]
	[DataRow("Elevator=")]
	[DataRow("Customize=")]
	[DataRow("CombineItems=")]
	public void ExemptCommand_RepeatsFreely(string command)
	{
		// VB6 never reports a repeat of a code of VetIgnora (10, 11, 12, 20 and 38), which these five commands hold (Module1.bas:1241).
		var checker = new Checker();

		Assert.IsTrue(checker.Register(4, command, 1, 2, 3));
		Assert.IsTrue(checker.Register(5, command, 1, 2, 3));

		DiagnosticAssert.NoDiagnostics(checker.Unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void OnePerSectionAndSecondArgumentRules_KeyAsVb6Does()
	{
		// VB6 keys a code of VetNoId on nothing, and one of VetNoId and VetDoppi on its second argument (Module1.bas:1236-1247, 6893-6896).
		var checker = new Checker();

		Assert.IsTrue(checker.Register(4, "Snow=", 1));
		Assert.IsFalse(checker.Register(5, "Snow=", 2));

		Assert.IsTrue(checker.Register(6, "LogItem=", 1, 5));
		Assert.IsTrue(checker.Register(7, "LogItem=", 1, 6));

		Assert.IsFalse(checker.Register(8, "LogItem=", 2, 5));

		DiagnosticAssert.Errors(
			checker.Unit.Diagnostics,
			(5, $"Duplicated command [Snow=] {Duplicate}"),
			(8, $"Duplicated command [LogItem=] {Duplicate}")
		);
	}
}
