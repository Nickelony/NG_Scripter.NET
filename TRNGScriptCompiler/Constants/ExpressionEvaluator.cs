using System.Text;
using TRNGScriptCompiler.Kernel.Numerics;

namespace TRNGScriptCompiler.Constants;

/// <summary>
/// Evaluates the numeric expressions of a script: literals and names joined by <c>+</c> and <c>-</c>,
/// read left to right.
/// </summary>
/// <remarks>
/// It splits at + then - as ConvertiStringaNumerica (Module1.bas:4830-4943); an operator with no value,
/// bad literal or 32-bit overflow fails (PD-51).
/// </remarks>
internal sealed class ExpressionEvaluator
{
	private readonly Func<string, string?, ConstantResolutionResult> _constantResolver;

	/// <summary>
	/// Initializes an evaluator whose resolver takes a name as written, matched without regard to case, and the file
	/// whose <c>#DEFINE</c> values it may use.
	/// </summary>
	internal ExpressionEvaluator(Func<string, string?, ConstantResolutionResult> constantResolver) => _constantResolver = constantResolver;

	/// <summary>
	/// Evaluates an expression such as <c>CONST1 + $10 - 5</c>, using the <c>#DEFINE</c> values of a non-null file.
	/// </summary>
	/// <remarks>
	/// The filter gets each term as written and its table (none for a literal) and says if it may appear;
	/// a <see langword="null"/> filter takes all. A rejection quotes the term in upper case.
	/// </remarks>
	internal NumericEvaluation Evaluate(string expression, string? file, Func<string, ConstantKind?, bool>? acceptTerm = null)
	{
		string text = expression.Trim();

		if (text.Length == 0)
			return Fail("the value is empty");

		// A sign may lead the expression; any other operator needs a value after it. VB6 applies the previous value again for an empty piece,
		// so 5+-3 reads 7 there (5, plus 5 again, minus 3), while an empty piece between two plus signs adds nothing, so 5++3 reads 8
		// (Module1.bas:4857-4911).
		if (HasOperatorWithoutValue(text))
		{
			string? fix = CollapseOperators(text);

			return Fail(
				fix is null
					? $"{text} has an operator with no value after it; write a number"
					: $"{text} has an operator with no value after it; write {fix}"
			);
		}

		long total = 0;
		int pluginId = 0;

		var terms = new TermReader(text);

		while (terms.MoveNext())
		{
			ReadOnlySpan<char> written = terms.Term;

			if (written.IsEmpty)
				continue;

			int value;
			ConstantKind? kind = null;
			string? term = null;

			if (NumericLiteral.IsLiteral(written))
			{
				if (!NumericLiteral.TryRead(written, out value, out string? error))
					return Fail(error);
			}
			else
			{
				term = AsString(text, written);
				ConstantResolutionResult resolved = _constantResolver(term, file);

				if (!resolved.IsKnown)
					return Fail($"{term} is neither a number nor a known name");

				// A name the caller refuses fails as refused, before the reason it does not resolve.
				if (!resolved.Success)
					return acceptTerm is not null && !acceptTerm(term, resolved.Kind) ? Reject(term, resolved.Kind) : Fail(resolved.Error);

				value = resolved.Value;
				kind = resolved.Kind;

				if (resolved.PluginId > 0)
					pluginId = resolved.PluginId;
			}

			if (acceptTerm is not null && !acceptTerm(term ??= AsString(text, written), kind))
				return Reject(term, kind);

			total = terms.Operator == '-' ? total - value : total + value;

			if (total is < int.MinValue or > int.MaxValue)
				return Fail($"{text} overflows 32 bits");
		}

		return new NumericEvaluation(Success: true, Value: (int)total, pluginId, Error: null, Rejection: null);
	}

	/// <summary>
	/// Resolves a name through the resolver callback alone, against the <c>#DEFINE</c> values of the file at the full
	/// path <paramref name="file"/>.
	/// </summary>
	internal ConstantResolutionResult ResolveName(string name, string file) => _constantResolver(name.Trim(), file);

	private static NumericEvaluation Fail(string? error) => new(Success: false, Value: 0, PluginId: 0, error, Rejection: null);

	private static NumericEvaluation Reject(string term, ConstantKind? kind) =>
		new(Success: false, Value: 0, PluginId: 0, Error: null, Rejection: new RejectedTerm(term.ToUpperInvariant(), kind));

	/// <summary>Gives a term as a string, which is <paramref name="text"/> itself when the term spans it.</summary>
	private static string AsString(string text, ReadOnlySpan<char> term) => term.Length == text.Length ? text : term.ToString();

	/// <summary>Reports whether an operator after the first term has no value after it.</summary>
	private static bool HasOperatorWithoutValue(ReadOnlySpan<char> text)
	{
		var terms = new TermReader(text);

		// The first term may be empty, since a sign may lead.
		terms.MoveNext();

		while (terms.MoveNext())
		{
			if (terms.Term.IsEmpty)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Builds the fix, folding each operator run into one sign and dropping a trailing one: 5+-3 suggests 5-3 and 5+
	/// suggests 5.
	/// </summary>
	private static string? CollapseOperators(ReadOnlySpan<char> text)
	{
		var fix = new StringBuilder();
		bool negative = false;
		var terms = new TermReader(text);

		while (terms.MoveNext())
		{
			negative ^= terms.Operator == '-';

			if (terms.Term.IsEmpty)
				continue;

			if (negative)
				fix.Append('-');
			else if (fix.Length > 0)
				fix.Append('+');

			fix.Append(terms.Term);
			negative = false;
		}

		return fix.Length > 0 ? fix.ToString() : null;
	}

	/// <summary>Walks the terms of an expression split at <c>+</c> and <c>-</c>, without copying the text.</summary>
	/// <remarks>
	/// Each term is trimmed, comes with the operator before it, <c>+</c> for the first, and may be empty.
	/// </remarks>
	private ref struct TermReader
	{
		private readonly ReadOnlySpan<char> _text;

		private int _start;
		private char _nextOperator;
		private bool _finished;

		internal TermReader(ReadOnlySpan<char> text)
		{
			_text = text;
			_nextOperator = '+';
		}

		/// <summary>Gets the operator before the current term.</summary>
		internal char Operator { get; private set; }

		/// <summary>Gets the current term, without the whitespace at its edges.</summary>
		internal ReadOnlySpan<char> Term { get; private set; }

		/// <summary>Moves to the next term.</summary>
		/// <returns><see langword="false"/> after the last term.</returns>
		internal bool MoveNext()
		{
			if (_finished)
				return false;

			ReadOnlySpan<char> rest = _text[_start..];
			int end = rest.IndexOfAny('+', '-');

			Operator = _nextOperator;

			if (end < 0)
			{
				Term = rest.Trim();
				_finished = true;
				return true;
			}

			Term = rest[..end].Trim();
			_nextOperator = rest[end];
			_start += end + 1;
			return true;
		}
	}
}
