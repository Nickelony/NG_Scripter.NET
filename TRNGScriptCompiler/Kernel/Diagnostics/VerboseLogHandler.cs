using System.Globalization;
using System.Runtime.CompilerServices;

namespace TRNGScriptCompiler.Kernel.Diagnostics;

/// <summary>Builds a verbose log line from an interpolated string, and nothing when the log is concise.</summary>
/// <remarks>
/// The compiler skips the appends, and the expressions in them, when <c>shouldAppend</c> is <see langword="false"/>.
/// It formats numbers with the invariant culture, so a line reads the same on every machine.
/// </remarks>
[InterpolatedStringHandler]
internal ref struct VerboseLogHandler
{
	private DefaultInterpolatedStringHandler _text;

	/// <summary>Initializes a handler that builds the text only when <paramref name="log"/> is not concise.</summary>
	/// <param name="literalLength">The length of the literal parts of the string, a size hint.</param>
	/// <param name="formattedCount">The number of expressions in the string, a size hint.</param>
	/// <param name="log">The log whose line the string becomes.</param>
	/// <param name="shouldAppend">Receives <see langword="false"/> when the log drops the line.</param>
	internal VerboseLogHandler(int literalLength, int formattedCount, CompilerLog log, out bool shouldAppend)
	{
		shouldAppend = !log.ConciseLogging;
		_text = shouldAppend ? new DefaultInterpolatedStringHandler(literalLength, formattedCount, CultureInfo.InvariantCulture) : default;
	}

	/// <summary>Appends the literal part of the string.</summary>
	internal void AppendLiteral(string value) => _text.AppendLiteral(value);

	/// <summary>Appends the value of an expression in the string.</summary>
	internal void AppendFormatted<T>(T value) => _text.AppendFormatted(value);

	/// <summary>Appends the value of an expression in the string, formatted.</summary>
	internal void AppendFormatted<T>(T value, string? format) => _text.AppendFormatted(value, format);

	/// <summary>Appends the value of an expression in the string, aligned.</summary>
	internal void AppendFormatted<T>(T value, int alignment) => _text.AppendFormatted(value, alignment);

	/// <summary>Appends the value of an expression in the string, aligned and formatted.</summary>
	internal void AppendFormatted<T>(T value, int alignment, string? format) => _text.AppendFormatted(value, alignment, format);

	/// <summary>Returns the text built and releases its buffer.</summary>
	/// <remarks>
	/// Call it once, and only for a handler that was asked to append.
	/// </remarks>
	internal string ToStringAndClear() => _text.ToStringAndClear();
}
