using System.Globalization;
using TRNGScriptCompiler.Kernel.Infrastructure;

namespace TRNGScriptCompiler;

/// <summary>
/// A main-language selector: a zero-based index into the <c>[Language]</c> list, or the text of a language file name
/// or base name.
/// </summary>
/// <remarks>
/// <see cref="FromText"/> reads numeric text as an index, other text as a full or base file name, and blank text
/// as no language. Two selectors are equal when they are both indexes of the same value, or both names that match
/// without regard to case.
/// </remarks>
public sealed class MainLanguageSelector : IEquatable<MainLanguageSelector>
{
	private enum SelectorKind
	{
		Index,
		Text,
	}

	private readonly SelectorKind _kind;

	private MainLanguageSelector(SelectorKind kind, int index, string text)
	{
		_kind = kind;
		Index = index;
		Text = text;
	}

	/// <summary>Gets a value indicating whether the selector is a zero-based list index.</summary>
	public bool IsIndex => _kind == SelectorKind.Index;

	/// <summary>
	/// Gets the selected index; only meaningful when <see cref="IsIndex"/> is <see langword="true"/>.
	/// </summary>
	public int Index { get; }

	/// <summary>
	/// Gets the selector text for diagnostics: the text without its outer blanks, or the index as rendered,
	/// so <c>007</c> reads <c>7</c>.
	/// </summary>
	public string Text { get; }

	/// <summary>Creates a selector for a zero-based index into the <c>[Language]</c> list.</summary>
	public static MainLanguageSelector FromIndex(int index) => new(SelectorKind.Index, index, index.ToString(CultureInfo.InvariantCulture));

	/// <summary>Parses the text form of a main-language selector.</summary>
	/// <param name="text">The selector text, or <see langword="null"/> for none.</param>
	/// <returns>The parsed selector, or <see langword="null"/> for blank text.</returns>
	public static MainLanguageSelector? FromText(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return null;

		string trimmed = text.Trim();

		return int.TryParse(s: trimmed, style: NumberStyles.Integer, provider: CultureInfo.InvariantCulture, result: out int index)
			? FromIndex(index)
			: new MainLanguageSelector(SelectorKind.Text, -1, trimmed);
	}

	/// <summary>Converts the text form to a selector, as <see cref="FromText"/> does.</summary>
	public static implicit operator MainLanguageSelector?(string? text) => FromText(text);

	/// <summary>Converts a zero-based language index to a selector.</summary>
	public static implicit operator MainLanguageSelector(int index) => FromIndex(index);

	/// <summary>Returns the selector text for diagnostics.</summary>
	public override string ToString() => Text;

	/// <inheritdoc/>
	public bool Equals(MainLanguageSelector? other)
	{
		if (other is null || _kind != other._kind)
			return false;

		return IsIndex ? Index == other.Index : string.Equals(Text, other.Text, StringComparison.OrdinalIgnoreCase);
	}

	/// <inheritdoc/>
	public override bool Equals(object? obj) => Equals(obj as MainLanguageSelector);

	/// <inheritdoc/>
	public override int GetHashCode() =>
		IsIndex ? HashCode.Combine(_kind, Index) : HashCode.Combine(_kind, StringComparer.OrdinalIgnoreCase.GetHashCode(Text));

	/// <summary>Returns whether two selectors are equal.</summary>
	public static bool operator ==(MainLanguageSelector? left, MainLanguageSelector? right) => left is null ? right is null : left.Equals(right);

	/// <summary>Returns whether two selectors differ.</summary>
	public static bool operator !=(MainLanguageSelector? left, MainLanguageSelector? right) => !(left == right);

	/// <summary>Returns whether the selector refers to a language file listed in the script.</summary>
	/// <remarks>
	/// An index selector never matches here, because the pipeline resolves it.
	/// </remarks>
	internal bool Matches(string languageFile)
	{
		if (_kind != SelectorKind.Text)
			return false;

		return PathUtilities.HaveSameFileName(Text, languageFile)
			|| Path.GetFileNameWithoutExtension(Text).IgnoreCaseEquals(Path.GetFileNameWithoutExtension(languageFile));
	}
}
