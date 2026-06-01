using System.Diagnostics.CodeAnalysis;
using TRNGScriptCompiler.Kernel.Numerics;

namespace TRNGScriptCompiler.Plugins;

/// <summary>
/// Reads the plugin ID of a <c>Plugin=</c> or <c>#DEFINE @PluginName</c> line as a $ or decimal number,
/// as VB6 does (<c>Module1.bas:13578-13598</c>, 15024-15104), and words the error for any other text.
/// </summary>
internal static class PluginIdReader
{
	/// <summary>Reads a trimmed plugin ID, naming the command or directive it belongs to in an error.</summary>
	/// <returns><see langword="false"/> after setting the error to the full message to report.</returns>
	internal static bool TryRead(string text, string command, out int id, [NotNullWhen(false)] out string? error)
	{
		id = 0;

		if (text.Length == 0 || !NumericLiteral.IsLiteral(text))
		{
			error = $"The plugin ID '{text}' of {command} is not a number; write the ID as a decimal or $ number";
			return false;
		}

		if (NumericLiteral.TryRead(text, out id, out string? reason))
		{
			error = null;
			return true;
		}

		error = $"The plugin ID '{text}' of {command} is invalid: {reason}";
		return false;
	}
}
