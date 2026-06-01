namespace TRNGScriptCompiler.Language;

/// <summary>The classic strings that one section header of a language file holds, each with its line.</summary>
internal sealed class ClassicStringTable
{
	private readonly List<string> _strings = [];
	private readonly List<int> _lines = [];

	/// <summary>Gets the strings in file order.</summary>
	internal IReadOnlyList<string> Strings => _strings;

	/// <summary>Gets the 1-based line of each string, in the order of <see cref="Strings"/>.</summary>
	internal IReadOnlyList<int> Lines => _lines;

	/// <summary>Gets or sets a value indicating whether the file has given this table its header.</summary>
	internal bool HasHeader { get; set; }

	internal void Add(string text, int line)
	{
		_strings.Add(text);
		_lines.Add(line);
	}
}
