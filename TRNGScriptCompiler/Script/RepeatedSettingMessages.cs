using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Script;

/// <summary>Words the diagnostics for a setting that a script gives twice (PD-31).</summary>
internal static class RepeatedSettingMessages
{
	/// <summary>Words the error for a second line that gives a one-value setting a different value.</summary>
	/// <remarks>
	/// The subject is the setting as the author wrote it, such as <c>LoadSave=</c>, and the earlier line is 1-based.
	/// </remarks>
	internal static string DifferentValue(string subject, SourceLocation earlier, string? currentFile) =>
		$"'{subject}' gives a value different from {SourceLocation.DescribeLine(earlier, currentFile)}, and the setting holds one value; keep one of the two lines";

	/// <summary>Words the warning for a second line that repeats a setting with the same value.</summary>
	/// <remarks>
	/// The subject is the setting as the author wrote it, such as <c>LoadSave=</c>, and the earlier line is 1-based.
	/// </remarks>
	internal static string SameValue(string subject, SourceLocation earlier, string? currentFile) =>
		$"'{subject}' repeats {SourceLocation.DescribeLine(earlier, currentFile)} with the same value; remove one of the two lines";

	/// <summary>
	/// Words the error for a second <c>Level=</c> line in one section, given the 1-based location of the first.
	/// </summary>
	internal static string SecondLevelLine(SourceLocation earlier, string? currentFile) =>
		$"The section gives 'Level=' at {SourceLocation.DescribeLine(earlier, currentFile)} already, and a second level path would point every later level at the wrong file; keep one of the two lines";
}
