using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TRNGScriptCompiler.Kernel.Numerics;

/// <summary>Reads a numeric literal by the rules under which VB6 and the port read the same value.</summary>
/// <remarks>
/// A literal VB6 reads differently, or one an author is likely to misread, fails with a reason that names
/// the fix (PD-51).
/// </remarks>
internal static class NumericLiteral
{
	/// <summary>Tells whether a term is written as a literal rather than as a name.</summary>
	/// <param name="term">The trimmed, non-empty term.</param>
	/// <returns><see langword="true"/> when the term starts with a digit, <c>$</c>, <c>#</c> or <c>&amp;</c>.</returns>
	internal static bool IsLiteral(ReadOnlySpan<char> term) => term[0] is '$' or '#' or '&' || char.IsAsciiDigit(term[0]);

	/// <summary>
	/// Reads an expression literal: decimal digits, <c>$</c> or <c>#</c> hexadecimal, or an <c>&amp;H</c>
	/// or <c>&amp;O</c> value.
	/// </summary>
	/// <remarks>
	/// The term is trimmed and passes <see cref="IsLiteral"/>; a literal the two compilers read differently fails
	/// with an error that names the fix.
	/// </remarks>
	internal static bool TryRead(ReadOnlySpan<char> term, out int value, [NotNullWhen(false)] out string? error) =>
		TryRead(term, acceptLongSuffix: false, out value, out error);

	/// <summary>
	/// Reads a standalone value: an optional sign before decimal digits, or a <c>$</c>,
	/// <c>&amp;H</c> or <c>&amp;O</c> value.
	/// </summary>
	/// <remarks>
	/// With <paramref name="acceptLongSuffix"/>, a VB literal takes the <c>&amp;</c> Long suffix, as where VB6 reads
	/// with <c>Val</c> alone.
	/// </remarks>
	internal static bool TryReadStandalone(string text, out int value, [NotNullWhen(false)] out string? error, bool acceptLongSuffix = false)
	{
		value = 0;
		string trimmed = text.Trim();

		if (trimmed.Length == 0)
		{
			error = "the value is empty";
			return false;
		}

		bool negative = trimmed[0] == '-';
		string unsigned = trimmed[0] is '-' or '+' ? trimmed[1..] : trimmed;

		// A language file and a constants file have no #DEFINE scope, so a value there takes no name and no sum, and #, which VB6 reads
		// as 0 there (Module1.bas:2107-2118, 3129-3135), fails.
		if (
			unsigned.Length == 0
			|| !IsLiteral(unsigned)
			|| unsigned[0] == '#'
			|| (unsigned.Length != trimmed.Length && !char.IsAsciiDigit(unsigned[0]))
		)
		{
			error = unsigned.StartsWith('#')
				? $"{trimmed} is not a number here; write ${unsigned[1..]} for a hexadecimal value"
				: $"{trimmed} is not a number; write decimal digits with an optional sign, or a $, &H or &O value";

			return false;
		}

		// The sign and the digits are read together, so -2147483648 reads as VB6's Val reads it into a Long.
		return unsigned.Length != trimmed.Length
			? TryReadDecimal(term: unsigned, negative, out value, out error)
			: TryRead(term: unsigned, acceptLongSuffix, out value, out error);
	}

	private static bool TryRead(ReadOnlySpan<char> term, bool acceptLongSuffix, out int value, [NotNullWhen(false)] out string? error)
	{
		// VB6 reads a $ or # literal as a 32-bit hexadecimal value (Module1.bas:4870-4884), and any other number through IsNumeric and
		// Val (:4886-4900), which read &H and &O literals too, and fractions in the Windows locale.
		value = 0;
		error = null;

		if (term[0] is '$' or '#')
			return TryReadHex(term, digits: term[1..], prefix: term[..1], out value, out error);

		if (term.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
			return TryReadVbLiteral(term, radix: 16, acceptLongSuffix, out value, out error);

		if (term.StartsWith("&O", StringComparison.OrdinalIgnoreCase))
			return TryReadVbLiteral(term, radix: 8, acceptLongSuffix, out value, out error);

		if (term[0] == '&')
		{
			error = $"{term} is not a number; a literal that starts with & is an &H hexadecimal or an &O octal value";
			return false;
		}

		return TryReadDecimal(term, negative: false, out value, out error);
	}

	private static bool TryReadHex(
		ReadOnlySpan<char> term,
		ReadOnlySpan<char> digits,
		ReadOnlySpan<char> prefix,
		out int value,
		[NotNullWhen(false)] out string? error
	)
	{
		value = 0;

		if (!TryAccumulate(digits, radix: 16, out ulong unsigned))
		{
			error = $"{term} is not a hexadecimal number; write 1 to 8 hexadecimal digits after {prefix}";
			return false;
		}

		if (unsigned > uint.MaxValue)
		{
			error = $"{term} does not fit 32 bits";
			return false;
		}

		value = unchecked((int)(uint)unsigned);
		error = null;
		return true;
	}

	/// <summary>
	/// Reads an <c>&amp;H</c> or <c>&amp;O</c> literal, which fails where VB's <c>Val</c> reads it as a negative value.
	/// </summary>
	private static bool TryReadVbLiteral(
		ReadOnlySpan<char> term,
		int radix,
		bool acceptLongSuffix,
		out int value,
		[NotNullWhen(false)] out string? error
	)
	{
		value = 0;

		bool longSuffix = term.Length > 2 && term[^1] == '&';
		ReadOnlySpan<char> digits = longSuffix ? term[2..^1] : term[2..];

		if (!TryAccumulate(digits, radix, out ulong unsigned))
		{
			error =
				$"{term} is not a{(radix == 16 ? " hexadecimal" : "n octal")} number; write {(radix == 16 ? "hexadecimal" : "octal")} digits after {term[..2]}";

			return false;
		}

		if (unsigned > uint.MaxValue)
		{
			error = $"{term} does not fit 32 bits";
			return false;
		}

		if (longSuffix && !acceptLongSuffix)
		{
			error = $"{term} ends in &, VB's Long suffix, which a number here does not take; write ${unsigned:X} for {unsigned}";
			return false;
		}

		// VB's Val reads a value up to &HFFFF as a 16-bit Integer and a larger one as a 32-bit Long, so &H8000-&HFFFF and
		// &H80000000-&HFFFFFFFF are negative. The $ form reads a Long through a trailing & (Module1.bas:4884), so only
		// &H80000000-&HFFFFFFFF are negative in it too. A trailing & on the &H form makes Val read a Long.
		long vbReading = unsigned switch
		{
			>= 0x8000 and <= 0xFFFF when !longSuffix => (short)unsigned,
			>= 0x80000000 => (int)(uint)unsigned,
			_ => (long)unsigned,
		};

		if (vbReading < 0)
		{
			error = string.Create(
				CultureInfo.InvariantCulture,
				$"VB reads {term} as {vbReading}; write ${unsigned:X} for {unsigned}, or {vbReading} for the value VB reads"
			);

			return false;
		}

		value = (int)vbReading;
		error = null;
		return true;
	}

	private static bool TryReadDecimal(ReadOnlySpan<char> term, bool negative, out int value, [NotNullWhen(false)] out string? error)
	{
		value = 0;

		if (!term.ContainsAnyExceptInRange('0', '9'))
		{
			if (
				long.TryParse(s: term, style: NumberStyles.None, provider: CultureInfo.InvariantCulture, result: out long magnitude)
				&& (negative ? -magnitude : magnitude) is >= int.MinValue and <= int.MaxValue
			)
			{
				value = (int)(negative ? -magnitude : magnitude);
				error = null;
				return true;
			}

			error = $"{(negative ? "-" : string.Empty)}{term} does not fit 32 bits";
			return false;
		}

		if (term.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			error = $"{term} is not a number; write ${term[2..]} for a hexadecimal value";
			return false;
		}

		error =
			term.IndexOfAny('.', ',', 'e') >= 0 || term.Contains('E')
				? $"{term} is not a whole number; write it without a fraction, a separator or an exponent"
				: $"{term} is not a number; a decimal number holds digits only";

		return false;
	}

	private static bool TryAccumulate(ReadOnlySpan<char> digits, int radix, out ulong value)
	{
		value = 0;

		if (digits.Length == 0)
			return false;

		foreach (char c in digits)
		{
			int digit = c switch
			{
				>= '0' and <= '9' => c - '0',
				>= 'A' and <= 'F' => c - 'A' + 10,
				>= 'a' and <= 'f' => c - 'a' + 10,
				_ => int.MaxValue,
			};

			if (digit >= radix)
				return false;

			// Leading zeros are allowed; a value past 32 bits stops growing, so the caller reports it.
			value = Math.Min((value * (ulong)radix) + (ulong)digit, (ulong)uint.MaxValue + 1);
		}

		return true;
	}
}
