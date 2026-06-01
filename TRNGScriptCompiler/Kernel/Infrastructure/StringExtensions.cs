namespace TRNGScriptCompiler.Kernel.Infrastructure;

internal static class StringExtensions
{
	internal static bool IgnoreCaseEquals(this string value, string? other) => value.Equals(other, StringComparison.OrdinalIgnoreCase);

	internal static bool IgnoreCaseStartsWith(this string value, string prefix) => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Removes everything from the first occurrence of <paramref name="commentPrefix"/>,
	/// such as <c>";"</c> or <c>"//"</c>, onward, then trims the result.
	/// </summary>
	/// <returns>The string without its comment, trimmed, whether the value holds a comment or not.</returns>
	internal static string StripInlineComment(this string value, string commentPrefix)
	{
		int index = value.IndexOf(commentPrefix, StringComparison.Ordinal);
		return (index >= 0 ? value[..index] : value).Trim();
	}
}
