using TRNGScriptCompiler.Kernel.Infrastructure;

namespace TRNGScriptCompiler.Constants;

/// <summary>
/// Reads the sections of a constants file or plugin script, each from a <c>&lt;START_...&gt;</c> line
/// to <c>&lt;END&gt;</c>.
/// </summary>
/// <remarks>
/// VB6 reads both in one loop that skips blank and <c>;</c> lines (Module1.bas:2513-2648),
/// and <c>&lt;END_CONSTANTS&gt;</c> is a section line.
/// </remarks>
internal static class ConstantsFileSectionReader
{
	/// <summary>
	/// Gets the lines of every section that a line equal to <paramref name="startMarker"/>,
	/// such as <c>&lt;START_CONSTANTS&gt;</c>, opens.
	/// </summary>
	/// <returns>
	/// Each line of the sections with its 1-based number, trimmed, without blank lines and <c>;</c> comment lines.
	/// </returns>
	internal static IEnumerable<(int LineNumber, string Text)> Read(IReadOnlyList<string> lines, string startMarker)
	{
		bool inSection = false;

		for (int i = 0; i < lines.Count; i++)
		{
			string text = lines[i].Trim();

			if (text.Length == 0 || text[0] == ';')
				continue;

			if (!inSection)
			{
				inSection = text.IgnoreCaseEquals(startMarker);
				continue;
			}

			if (text.IgnoreCaseEquals("<END>"))
			{
				inSection = false;
				continue;
			}

			yield return (i + 1, text);
		}
	}
}
