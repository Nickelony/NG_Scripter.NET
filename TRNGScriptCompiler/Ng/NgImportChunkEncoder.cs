using System.Runtime.InteropServices;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Encodes the NG import-file section: one chunk per imported file,
/// carrying the import ID, mode, file type, file number, padded file name, size, and payload words.
/// </summary>
internal sealed class NgImportChunkEncoder
{
	private readonly CompilerLog _logger;
	private readonly DiagnosticSink _diagnostics;

	internal NgImportChunkEncoder(CompilerLog logger, DiagnosticSink diagnostics)
	{
		_logger = logger;
		_diagnostics = diagnostics;
	}

	/// <summary>Encodes all import-file entries into the NG header word list.</summary>
	/// <returns>
	/// <see langword="true"/> when every entry was encoded; otherwise, <see langword="false"/>, after an error.
	/// </returns>
	internal bool TryEncodeAll(List<short> allWords, IReadOnlyList<NgImportFile> importFiles)
	{
		if (importFiles.Count == 0)
			return true;

		_logger.LogSeparator();

		_logger.LogVerbose($"Files to import: {importFiles.Count}");
		_logger.LogVerbose("---------------------------------------------------------");

		foreach (NgImportFile importFile in importFiles)
		{
			if (!TryEncode(allWords, importFile))
				return false;
		}

		return true;
	}

	/// <summary>Encodes one import-file chunk into the NG header word list.</summary>
	/// <returns>
	/// <see langword="true"/> when the entry was encoded; otherwise, <see langword="false"/>, after an error.
	/// </returns>
	internal bool TryEncode(List<short> allWords, NgImportFile importFile)
	{
		// A file system can hold a character Windows-1252 lacks in a name, although a script needs a byte-order mark to write one (see Vb6Ansi.IsLossy).
		if (Vb6Ansi.IsLossy(importFile.FileName))
		{
			_diagnostics.Error(Vb6Ansi.FormatLossyError(importFile.FileName), importFile.Location);
			return false;
		}

		if (!FileNameNumber.TryParse(importFile.FileName, out int fileNumber, out string? error))
		{
			_diagnostics.Error(error, importFile.Location);
			return false;
		}

		int fileSize = importFile.Data.Length;

		List<short> payload = [(short)importFile.ImportId, (short)importFile.ImportMode, (short)importFile.FileType, (short)fileNumber];
		payload.AddFixedLengthText(importFile.FileName + "\0", NgImportFile.NameFieldWords);
		payload.AddDWord(fileSize);
		payload.AddPackedBytes(importFile.Data);

		// The chunk always takes the long size form, which the goldens hold.
		allWords.AddChunk(unchecked((short)NgTagType.ImportFile), CollectionsMarshal.AsSpan(payload), longForm: true);

		// VB6 copies the payload as TotBytes / 2, rounded half to even, plus one word for an odd size (Module1.bas:6837-6838),
		// so a size that is 3 mod 4 gains one zero word past the size the chunk declares (Module1.bas:8607-8610).
		if (fileSize % 4 == 3)
			allWords.Add(0);

		_logger.LogVerbose($"Import file: {importFile.FileName} ({fileSize} bytes)", indent: 1);
		return true;
	}
}
