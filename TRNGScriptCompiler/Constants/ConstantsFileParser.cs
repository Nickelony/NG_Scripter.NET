using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Numerics;

namespace TRNGScriptCompiler.Constants;

/// <summary>
/// Parses the <c>&lt;START_CONSTANTS&gt;</c> lines of a constants file or a plugin script, which share one grammar
/// as in VB6.
/// </summary>
/// <remarks>
/// <c>;</c> starts a comment, one <c>:</c> parts the trimmed name and value (Module1.bas:3076-3140), and a value VB6
/// reads otherwise fails (PD-62).
/// </remarks>
internal static class ConstantsFileParser
{
	/// <summary>
	/// Parses the constants of a file, reporting a malformed value as an error and each line it skips as a warning.
	/// </summary>
	/// <remarks>
	/// Names match without regard to case, and a name given twice keeps its first definition, as the VB6 lookup does.
	/// </remarks>
	internal static IReadOnlyDictionary<string, ConstantDefinition> Parse(IReadOnlyList<string> lines, string filePath, DiagnosticSink diagnostics)
	{
		var constants = new Dictionary<string, ConstantDefinition>(StringComparer.OrdinalIgnoreCase);

		foreach ((int lineNumber, string text) in ConstantsFileSectionReader.Read(lines, "<START_CONSTANTS>"))
		{
			var location = new SourceLocation(lineNumber, File: filePath);
			string line = text.StripInlineComment(";");

			if (line.Length == 0)
				continue;

			string[] parts = line.Split(':');

			// VB6 shows a message box for such a line and goes on without it (:3094-3110).
			if (parts.Length != 2)
			{
				diagnostics.Warning(
					$"The constants line '{line}' is skipped, because a constant takes one ':' between its name and its value; write NAME:VALUE",
					location
				);

				continue;
			}

			string name = parts[0].Trim();
			string valueText = parts[1].Trim();

			if (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_') || name.Any(char.IsWhiteSpace))
			{
				diagnostics.Warning($"The constant name '{name}' is skipped, because a name starts with a letter or _ and holds no space", location);
				continue;
			}

			// VB6 reads $ hexadecimal only right after the colon and the rest with Val (:3129-3135), so N: $10 is 0 and N:12abc is 12, and
			// Val reads VB's & Long suffix, so N:&HFFFF& is 65535, as the port reads it too.
			if (!NumericLiteral.TryReadStandalone(text: valueText, out int value, error: out string? reason, acceptLongSuffix: true))
			{
				diagnostics.Error($"The value of the constant {name} is invalid: {reason}", location);
				continue;
			}

			if (constants.TryGetValue(name, out ConstantDefinition first))
			{
				diagnostics.Warning($"The constant {name} repeats line {first.Line}, whose value is used; remove one of the two lines", location);
				continue;
			}

			constants.Add(name, new ConstantDefinition(value, lineNumber));
		}

		return constants;
	}
}
