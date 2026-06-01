using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Model;

/// <summary>Import file data (for ImportFile= commands).</summary>
internal sealed record NgImportFile
{
	/// <summary>The size of the file-name field of an import chunk in words, its terminator included.</summary>
	internal const int NameFieldWords = 40;

	/// <summary>The size of the file-name field of an import chunk in bytes, its terminator included.</summary>
	internal const int NameFieldBytes = NameFieldWords * 2;

	/// <summary>Gets the import identifier written to the NG header.</summary>
	internal required int ImportId { get; init; }

	/// <summary>Gets the file name stored for the imported payload.</summary>
	internal required string FileName { get; init; }

	/// <summary>Gets the import mode that controls the payload lifetime.</summary>
	internal required NgImportMode ImportMode { get; init; }

	/// <summary>Gets the numeric file type written for the import.</summary>
	internal required int FileType { get; init; }

	/// <summary>Gets the location of the <c>ImportFile=</c> line.</summary>
	internal SourceLocation Location { get; init; }

	internal required byte[] Data { get; init; }
}
