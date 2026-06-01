using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Resolves and validates classic command arguments, both VB6 numeric expressions and string references,
/// as their single owner.
/// </summary>
/// <remarks>
/// Numbers take hex literals, constants, file-scoped defines, sums and differences; strings take indexes
/// and binary literals, and reject <c>!NNN</c> references (PD-11).
/// </remarks>
internal sealed class ScriptArgumentResolver
{
	private readonly DiagnosticSink _diagnostics;
	private readonly ExpressionEvaluator _evaluator;
	private readonly StringReferenceResolver _strings;

	/// <param name="diagnostics">
	/// The per-run sink that receives an error for each argument that does not resolve.
	/// </param>
	/// <param name="evaluator">
	/// The evaluator that reads numeric arguments with the run's constants, slot, static and <c>#DEFINE</c> names.
	/// </param>
	/// <param name="languageData">The first language, whose indexes the string references resolve to.</param>
	internal ScriptArgumentResolver(DiagnosticSink diagnostics, ExpressionEvaluator evaluator, LanguageData languageData)
	{
		_diagnostics = diagnostics;
		_evaluator = evaluator;
		_strings = new StringReferenceResolver(languageData, diagnostics);
	}

	/// <summary>
	/// Silently evaluates a numeric argument in the <c>#DEFINE</c> scope of the full
	/// path <paramref name="currentFile"/>.
	/// </summary>
	/// <remarks>
	/// VB6's <c>GetArgNumerico</c> reads it, taking TRNG constants, slot, static and <c>#DEFINE</c> names and sums
	/// (<c>Module1.bas:4886-4897</c>).
	/// </remarks>
	internal bool TryEvaluate(string argument, string? currentFile, out int value)
	{
		NumericEvaluation evaluation = _evaluator.Evaluate(argument, currentFile);
		value = evaluation.Value;
		return evaluation.Success;
	}

	/// <summary>
	/// Evaluates a numeric argument and reports a failure, numbering the zero-based <paramref name="index"/> from 1
	/// as VB6 does.
	/// </summary>
	/// <remarks>
	/// The file of <paramref name="location"/> scopes the <c>#DEFINE</c> values the argument may use.
	/// </remarks>
	internal bool TryEvaluateArgument(IReadOnlyList<string> args, int index, string command, SourceLocation location, out int value)
	{
		NumericEvaluation evaluation = _evaluator.Evaluate(args[index], location.File);
		value = evaluation.Value;

		if (evaluation.Success)
			return true;

		_diagnostics.Error($"Argument {index + 1} of {command} is invalid: {evaluation.Error}", location);
		return false;
	}

	/// <summary>
	/// Resolves a classic string reference, never an ExtraNG one, to its language index, or <c>-1</c> after an error.
	/// </summary>
	/// <remarks>
	/// VB6 rejects a <c>!NNN</c> reference in a classic command and looks up only the classic
	/// strings (<c>Module1.bas:6548-6553</c>, <c>6597</c>).
	/// </remarks>
	internal int ResolveStringIndex(string text, string notFoundMessage, SourceLocation location) =>
		_strings.TryResolve(text, allowExtraNg: false, notFoundMessage, location, out int index) ? index : -1;

	/// <summary>Validates that a string index fits the one byte that <c>Name=</c> and <c>Legend=</c> write.</summary>
	/// <remarks>
	/// VB6 cuts the index to one byte (<c>Module1.bas:5851</c>, <c>5994</c>), so the game shows
	/// string <c>index - 256</c>.
	/// </remarks>
	internal bool ValidateOneByteStringIndex(int index, string text, string command, SourceLocation location)
	{
		if (index <= byte.MaxValue)
			return true;

		_diagnostics.Error(
			$"The {command} string '{text}' is entry {index} of the language file, but {command} writes its index in one byte; move the string into the first 256 entries",
			location
		);

		return false;
	}

	/// <summary>Checks that an argument list has the expected number of entries.</summary>
	/// <remarks>
	/// Every command that counts its arguments reports a mismatch with this one message, so each reads alike.
	/// </remarks>
	internal static bool CheckArgumentCount(
		DiagnosticSink diagnostics,
		IReadOnlyList<string> args,
		int expected,
		string command,
		SourceLocation location
	)
	{
		if (args.Count != expected)
		{
			diagnostics.Error(ArgumentCountMessage.Wrong(command, expected, args.Count), location);
			return false;
		}

		return true;
	}

	/// <summary>Validates that a value fits a byte (-128 to 255) or a word (-32768 to 65535).</summary>
	/// <remarks>
	/// VB6 takes -256 to 255 as a byte (Module1.bas:4915-4937); the port rejects what only wrapping fits (PD-56),
	/// and the argument is zero-based.
	/// </remarks>
	internal bool ValidateWidth(int value, int widthInBytes, string command, int argumentIndex, SourceLocation location)
	{
		if (!FieldRange.Fits(value, widthInBytes, minimum: out long min, maximum: out long max))
		{
			string holder = widthInBytes == 1 ? "its byte" : "its word";

			_diagnostics.Error(
				FieldRange.DescribeArgument(command, argumentNumber: argumentIndex + 1, value, minimum: min, maximum: max, holder),
				location
			);

			return false;
		}

		return true;
	}
}
