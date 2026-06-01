namespace TRNGScriptCompiler.Script;

/// <summary>Splits a normalized command line into its parts, and reads the raw text after its <c>=</c>.</summary>
internal static class ScriptLineLexer
{
	/// <summary>
	/// Splits a line into its command name, with its <c>=</c>, and trimmed arguments, or an empty command
	/// when it lacks <c>=</c>.
	/// </summary>
	/// <remarks>
	/// VB6 splits at every comma and gives a double quote no meaning (<c>Module1.bas:4729-4761</c>), so a name can hold
	/// a quote or an inch mark.
	/// </remarks>
	internal static (string Command, List<string> Arguments) ParseCommandLine(string line)
	{
		int equalsIndex = line.IndexOf('=');

		if (equalsIndex < 0)
			return (string.Empty, new List<string>());

		// "InputTimeOut =" reads as "InputTimeOut=", so a caller matches one spelling without regard to case.
		int commandEnd = equalsIndex;

		while (commandEnd > 0 && char.IsWhiteSpace(line[commandEnd - 1]))
			commandEnd--;

		string command = commandEnd == equalsIndex ? line[..(equalsIndex + 1)] : line[..commandEnd] + "=";
		string argsText = line[(equalsIndex + 1)..].Trim();

		var arguments = new List<string>();

		if (argsText.Length == 0)
			return (command, arguments);

		int segmentStart = 0;

		for (int i = 0; i < argsText.Length; i++)
		{
			if (argsText[i] == ',')
			{
				arguments.Add(argsText[segmentStart..i].Trim());
				segmentStart = i + 1;
			}
		}

		// A trailing comma leaves an empty last argument (Module1.bas:4754-4755), which the argument count catches.
		arguments.Add(argsText[segmentStart..].Trim());

		return (command, arguments);
	}

	/// <summary>
	/// Returns the text after a line's first <c>=</c>, with the whitespace at its edges trimmed as an argument's
	/// is (PD-63).
	/// </summary>
	/// <remarks>
	/// VB6 reads <c>Name=</c> and <c>Legend=</c> this way, so a comma is part of their text; a line without <c>=</c>
	/// gives an empty string.
	/// </remarks>
	internal static string GetRawRemainder(string line)
	{
		int equalsIndex = line.IndexOf('=');
		return equalsIndex < 0 ? string.Empty : line[(equalsIndex + 1)..].Trim();
	}
}
