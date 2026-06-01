using System.Diagnostics.CodeAnalysis;

namespace TRNGScriptCompiler.Kernel.Runtime;

/// <summary>
/// Holds a run's <c>#DEFINE</c> values per declaring file, keyed by full path, with names that match without regard
/// to case.
/// </summary>
/// <remarks>
/// VB6 keys on the bare file name (<c>Module1.bas:10085-10091</c>), unique only because it opens every include
/// from the script folder (PD-16).
/// </remarks>
internal sealed class DefineRegistry
{
	private readonly Dictionary<string, Dictionary<string, DefineRecord>> _definesByFile = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Dictionary<string, int>> _invalidLinesByFile = new(StringComparer.OrdinalIgnoreCase);

	private string _lastFile = string.Empty;
	private string _lastFullPath = string.Empty;

	/// <summary>
	/// Gives the full path that keys a file, resolving the file of the last call only once, since a script reads
	/// the lines of one file in a row.
	/// </summary>
	/// <remarks>
	/// A relative path resolves against the working directory of the call that resolved it.
	/// </remarks>
	private string FullPathOf(string file)
	{
		if (!string.Equals(file, _lastFile, StringComparison.Ordinal))
		{
			_lastFullPath = Path.GetFullPath(file);
			_lastFile = file;
		}

		return _lastFullPath;
	}

	/// <summary>
	/// Finds the line of a <c>#DEFINE</c> a file declares under a name and that gives no value, because the line
	/// is in error.
	/// </summary>
	/// <remarks>
	/// The line is 1-based.
	/// </remarks>
	internal bool TryGetInvalid(string file, string name, out int lineNumber)
	{
		lineNumber = 0;
		return _invalidLinesByFile.TryGetValue(FullPathOf(file), out Dictionary<string, int>? lines) && lines.TryGetValue(name, out lineNumber);
	}

	/// <summary>Records a name whose <c>#DEFINE</c> line failed, so a use of the name points at that line.</summary>
	/// <remarks>
	/// <paramref name="lineNumber"/> is 1-based, and the first failed line of a name is kept.
	/// </remarks>
	internal void AddInvalid(string file, string name, int lineNumber)
	{
		string fullPath = FullPathOf(file);

		if (!_invalidLinesByFile.TryGetValue(fullPath, out Dictionary<string, int>? lines))
		{
			lines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			_invalidLinesByFile.Add(fullPath, lines);
		}

		lines.TryAdd(name, lineNumber);
	}

	internal bool TryGet(string file, string name, [NotNullWhen(true)] out DefineRecord? define)
	{
		define = null;
		return _definesByFile.TryGetValue(FullPathOf(file), out Dictionary<string, DefineRecord>? defines) && defines.TryGetValue(name, out define);
	}

	/// <summary>Adds a define to the scope of its file, which does not declare its name yet.</summary>
	/// <param name="define">The define, whose location names the file that scopes it.</param>
	internal void Add(DefineRecord define)
	{
		string file = define.Location.File ?? throw new ArgumentException("A define names the file that scopes it.", nameof(define));
		string fullPath = FullPathOf(file);

		if (!_definesByFile.TryGetValue(fullPath, out Dictionary<string, DefineRecord>? defines))
		{
			defines = new Dictionary<string, DefineRecord>(StringComparer.OrdinalIgnoreCase);
			_definesByFile.Add(fullPath, defines);
		}

		defines.Add(define.Name, define);
	}
}
