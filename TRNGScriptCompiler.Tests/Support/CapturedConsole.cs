using System.Text;
using TRNGScriptCompiler.Cli;

namespace TRNGScriptCompiler.Tests.Support;

/// <summary>Records what the console host writes to each stream, with the color of each piece.</summary>
/// <param name="input">The text the application reads its answers from.</param>
internal sealed class CapturedConsole(string input = "")
{
	private readonly StringBuilder _all = new();
	private readonly StringBuilder _output = new();
	private readonly StringBuilder _error = new();

	private readonly Dictionary<ConsoleColor, StringBuilder> _byColor = [];

	/// <summary>Gets what both streams received, in the order of the writes.</summary>
	internal string AllText => _all.ToString();

	internal string OutputText => _output.ToString();

	internal string ErrorText => _error.ToString();

	internal string TextIn(ConsoleColor color) => _byColor.TryGetValue(color, out StringBuilder? text) ? text.ToString() : string.Empty;

	/// <summary>Gets the lines written to either stream, each without its line break.</summary>
	internal string[] AllLines => SplitLines(AllText);

	/// <summary>Gets the lines written to standard output, each without its line break.</summary>
	internal string[] OutputLines => SplitLines(OutputText);

	/// <summary>Gets the lines written to standard error, each without its line break.</summary>
	internal string[] ErrorLines => SplitLines(ErrorText);

	/// <summary>Gets the lines written in a color, each without its line break.</summary>
	internal string[] LinesIn(ConsoleColor color) => SplitLines(TextIn(color));

	// Each test's output ends in a line break, so the empty piece after the last one is dropped.
	private static string[] SplitLines(string text) => text.ReplaceLineEndings("\n").Split('\n')[..^1];

	internal CliApplication CreateApplication(CancellationToken cancellationToken = default, Action? compileStarted = null) =>
		new(input: new StringReader(input), Write, WriteError, compileStarted, cancellationToken);

	/// <summary>
	/// Creates the process wrapper of the host over this console, whose wait for a key runs the action given.
	/// </summary>
	internal CliProcess CreateProcess(Action waitForExit) => new(WriteError, waitForExit);

	private void Write(string text, ConsoleColor? color) => Record(_output, text, color);

	private void WriteError(string text, ConsoleColor? color) => Record(_error, text, color);

	private void Record(StringBuilder stream, string text, ConsoleColor? color)
	{
		_all.Append(text);
		stream.Append(text);

		if (color is { } value)
		{
			if (!_byColor.TryGetValue(value, out StringBuilder? colored))
				_byColor[value] = colored = new StringBuilder();

			colored.Append(text);
		}
	}
}
