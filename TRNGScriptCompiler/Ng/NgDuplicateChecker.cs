using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Fails an NG command that repeats an earlier line of its section under the command's duplicate rule.
/// </summary>
internal sealed class NgDuplicateChecker
{
	private readonly DiagnosticSink _diagnostics;
	private readonly NgCommandEncoder _encoder;

	/// <param name="diagnostics">The per-run sink that receives the error for a duplicate.</param>
	/// <param name="encoder">
	/// The encoder that finds the index a string argument resolves to, because the rule keys on that index.
	/// </param>
	internal NgDuplicateChecker(DiagnosticSink diagnostics, NgCommandEncoder encoder)
	{
		_diagnostics = diagnostics;
		_encoder = encoder;
	}

	/// <summary>
	/// Registers a command in its section, failing when it repeats an earlier line under its duplicate rule.
	/// </summary>
	/// <remarks>
	/// VB6 compares the resolved values of the arguments the rule names, with -1 read
	/// as 0 (Module1.bas:6866-6930, 7592-7604).
	/// </remarks>
	internal bool TryRegister(
		NgSectionState sectionState,
		NgCommandDefinition definition,
		List<NgArgumentValue> commandArgs,
		SourceLocation nameLocation
	)
	{
		NgDuplicateRule rule = NgCommandPolicies.Get(definition).Duplicates;

		if (rule == NgDuplicateRule.Exempt)
			return true;

		(int firstValue, int secondValue) = rule switch
		{
			NgDuplicateRule.OnePerSection => (0, 0),
			NgDuplicateRule.SecondArgument => (0, GetArgumentValue(commandArgs, 1)),
			NgDuplicateRule.FirstTwoArguments => (GetArgumentValue(commandArgs, 0), GetArgumentValue(commandArgs, 1)),
			NgDuplicateRule.SlotAndAnimation => (GetArgumentValue(commandArgs, 0), GetArgumentValue(commandArgs, 2)),
			_ => (GetArgumentValue(commandArgs, 0), 0),
		};

		if (firstValue == -1)
			firstValue = 0;

		if (sectionState.TryRegisterDuplicateKey(definition.Name, $"{firstValue}|{secondValue}"))
			return true;

		string duplicateSuffix = rule switch
		{
			NgDuplicateRule.OnePerSection or NgDuplicateRule.SecondArgument => string.Empty,
			NgDuplicateRule.FirstTwoArguments => FormattableString.Invariant($" {firstValue}, {secondValue}, ..."),
			NgDuplicateRule.SlotAndAnimation => FormattableString.Invariant($" {firstValue}, {GetArgumentValue(commandArgs, 1)}, {secondValue}, ..."),
			_ => FormattableString.Invariant($" {firstValue}, ..."),
		};

		_diagnostics.Error(
			$"Duplicated command [{definition.Name}{duplicateSuffix}] was already present in same [level/title/option] section",
			nameLocation
		);

		return false;
	}

	private int GetArgumentValue(List<NgArgumentValue> commandArgs, int argumentIndex)
	{
		if (argumentIndex >= commandArgs.Count)
			return 0;

		NgArgumentValue arg = commandArgs[argumentIndex];

		if (arg.Type == NgArgumentType.Bool)
			return arg.BoolValue ? 1 : 0;

		// VB6 keys a STRING argument on the index it resolves to (Module1.bas:7592-7604).
		if (arg.Type == NgArgumentType.String)
			return arg.IsDiscard ? -1 : _encoder.FindStringIndex(arg.StringValue);

		return arg.Int32Value;
	}
}
