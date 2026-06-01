using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// The header line that opens each section of <c>Script.txt</c>, keyed by <see cref="ScriptSectionType"/>.
/// </summary>
internal static class ScriptSectionHeaders
{
	private static readonly ScriptSectionType[] s_headedSections =
	[
		ScriptSectionType.PsxExtensions,
		ScriptSectionType.PcExtensions,
		ScriptSectionType.Language,
		ScriptSectionType.Options,
		ScriptSectionType.Title,
		ScriptSectionType.Level,
	];

	/// <summary>Gets the header of a section, such as <c>[Level]</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="sectionType"/> is <see cref="ScriptSectionType.None"/>, which has no header.
	/// </exception>
	internal static string Of(ScriptSectionType sectionType)
	{
		return sectionType switch
		{
			ScriptSectionType.PsxExtensions => "[PSXExtensions]",
			ScriptSectionType.PcExtensions => "[PCExtensions]",
			ScriptSectionType.Language => "[Language]",
			ScriptSectionType.Options => "[Options]",
			ScriptSectionType.Title => "[Title]",
			ScriptSectionType.Level => "[Level]",
			_ => throw new ArgumentOutOfRangeException(nameof(sectionType), sectionType, "The section type has no header."),
		};
	}

	/// <summary>Finds the section a normalized script line opens, without regard to case.</summary>
	/// <returns>The section the line is the header of; otherwise, <see cref="ScriptSectionType.None"/>.</returns>
	internal static ScriptSectionType Find(string line)
	{
		foreach (ScriptSectionType sectionType in s_headedSections)
		{
			if (line.IgnoreCaseEquals(Of(sectionType)))
				return sectionType;
		}

		return ScriptSectionType.None;
	}
}
