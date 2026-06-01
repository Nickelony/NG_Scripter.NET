namespace TRNGScriptCompiler.Model;

internal enum NgImportMode
{
	/// <summary>The script gave mode 0, which names neither mode.</summary>
	None = 0,

	/// <summary>The imported payload should remain resident in memory.</summary>
	Memory = 1,

	/// <summary>The imported payload should be treated as temporary.</summary>
	Temporary = 2,
}
