using System.Diagnostics.CodeAnalysis;
using System.Text;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Script;

/// <summary>Reads <c>Script.txt</c> and nested include files using the TRNG include rules.</summary>
internal sealed class ScriptSourceLoader
{
	/// <summary>The most include files one run loads, a file included twice counting twice.</summary>
	internal const int MaxIncludeFiles = 10_000;

	/// <summary>The most include files one chain nests, because VB6's include stack holds 21 frames.</summary>
	/// <remarks>
	/// The stack holds <c>Script.txt</c> and 20 more files (<c>Mod_ScriptDat.bas:131</c>).
	/// </remarks>
	private const int MaxNestedIncludes = 20;

	private readonly CompilationRun _run;
	private readonly string _scriptDirectory;
	private readonly VirtualFileSystem? _overlay;
	private readonly List<SourceFileState> _fileStack;
	private readonly List<(string FilePath, IReadOnlyList<string> Lines)> _loadedFiles = [];
	private readonly HashSet<string> _filesWithEncodingNote = new(StringComparer.OrdinalIgnoreCase);

	private int _includeCount;

	/// <remarks>
	/// The script path is the full path to <c>Script.txt</c>. VB6 looks for an include in the script folder, and a file
	/// the overlay of unsaved editor buffers holds is read from memory, not disk.
	/// </remarks>
	internal ScriptSourceLoader(CompilationRun run, string scriptPath, string scriptDirectory, VirtualFileSystem? overlay = null)
	{
		_run = run;
		_fileStack = [new SourceFileState(scriptPath)];
		_scriptDirectory = scriptDirectory;
		_overlay = overlay;
	}

	/// <summary>
	/// Gets the files read so far with their lines, in the order they were read; a file included twice appears twice.
	/// </summary>
	internal IReadOnlyList<(string FilePath, IReadOnlyList<string> Lines)> LoadedFiles => _loadedFiles;

	/// <summary>Gets the error of the last read that failed, or <see langword="null"/> when none has.</summary>
	internal ScriptReadError? Error { get; private set; }

	internal bool HasPendingFiles => _fileStack.Count > 0;

	/// <summary>
	/// Reads the next logical source line, traversing include files and line continuations as needed.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when a line was read; <see langword="false"/> when the input ended or the read failed,
	/// with the cause of a failure in <see cref="Error"/>.
	/// </returns>
	internal bool TryReadNext([NotNullWhen(true)] out ScriptSourceLine? sourceLine, CancellationToken token = default)
	{
		sourceLine = null;

		while (_fileStack.Count > 0)
		{
			token.ThrowIfCancellationRequested();

			SourceFileState currentFile = _fileStack[^1];

			if (!currentFile.IsLoaded)
			{
				if (!TryLoadFileContent(currentFile))
					return false;

				_run.Logger.LogVerbose($"------------ PARSING FILE <{Path.GetFileName(currentFile.FilePath)}> ----------");
			}

			if (currentFile.LineIndex >= currentFile.Lines.Length)
			{
				_fileStack.RemoveAt(_fileStack.Count - 1);
				_run.Logger.LogVerbose($"------------ END FILE <{Path.GetFileName(currentFile.FilePath)}> ----------");
				continue;
			}

			int lineIndex = currentFile.LineIndex;
			currentFile.LineIndex++;
			currentFile.LineNumber = lineIndex + 1;

			if (!TryJoinContinuedLines(currentFile, out string rawLine))
				return false;

			// VB6 reports a joined line at its first physical line (Glob.NLinea, Module1.bas:5147-5148).
			sourceLine = new ScriptSourceLine(
				Text: rawLine,
				SourceText: ScriptLineText.WithoutComment(currentFile.Lines[currentFile.LineNumber - 1]),
				new SourceLocation(currentFile.LineNumber, File: currentFile.FilePath)
			);

			return true;
		}

		return false;
	}

	/// <summary>
	/// Joins the lines a <c>&gt;</c> continues, cutting each piece at its <c>&gt;</c> and trimming its spaces.
	/// </summary>
	/// <remarks>
	/// Pieces join with nothing between, as in VB6 (<c>Module1.bas:5149-5167</c>), and an error points at the line
	/// of the offending <c>&gt;</c>.
	/// </remarks>
	private bool TryJoinContinuedLines(SourceFileState file, out string line)
	{
		line = file.Lines[file.LineIndex - 1];

		int markerIndex = FindContinuationMarker(line);

		if (markerIndex < 0)
			return true;

		string piece = line;
		var joined = new StringBuilder();

		int markerLineNumber = file.LineNumber;

		while (true)
		{
			int commentIndex = piece.IndexOf("//", StringComparison.Ordinal);

			// Only a ';' comment stops a continuation (Module1.bas:5158), so a '>' after '//' would join the next line into the comment.
			if (commentIndex >= 0 && commentIndex < markerIndex)
			{
				return Fail(
					file,
					markerLineNumber,
					"A '>' inside a '//' comment continues the line and swallows the next one; start the comment with ';' or remove the '>'"
				);
			}

			string tail = piece[(markerIndex + 1)..].Trim();

			if (tail.Length > 0 && !tail.StartsWith(';') && !tail.StartsWith("//", StringComparison.Ordinal))
			{
				return Fail(
					file,
					markerLineNumber,
					$"The text '{tail}' after a '>' continuation would be dropped; move it before the '>' or onto the next line"
				);
			}

			joined.Append(piece[..markerIndex].Trim(' '));

			if (file.LineIndex >= file.Lines.Length)
				return Fail(file, markerLineNumber, "A '>' continuation on the last line of the file has no line to join; remove the '>'");

			piece = file.Lines[file.LineIndex];
			file.LineIndex++;
			markerIndex = FindContinuationMarker(piece);

			string next = (markerIndex < 0 ? piece : piece[..markerIndex]).Trim(' ');

			if (!IsLegalJoin(joined, next))
			{
				return Fail(
					file,
					markerLineNumber,
					"A '>' continuation joins the next line with nothing between, so here it splits a value or a word; break the line next to a comma or right after the '='"
				);
			}

			if (markerIndex < 0)
			{
				line = joined.Append(next).ToString();
				return true;
			}

			markerLineNumber = file.LineIndex;
		}
	}

	/// <summary>
	/// Returns the index of the <c>&gt;</c> that continues a line, or -1 when the line
	/// has none before a <c>;</c> comment.
	/// </summary>
	private static int FindContinuationMarker(string piece)
	{
		int markerIndex = piece.IndexOf('>');
		int commentIndex = piece.IndexOf(';');

		return markerIndex >= 0 && (commentIndex < 0 || markerIndex < commentIndex) ? markerIndex : -1;
	}

	/// <summary>
	/// Returns whether a join is legal, next to a comma or right after the command's <c>=</c>, where it splits no value
	/// or word.
	/// </summary>
	private static bool IsLegalJoin(StringBuilder joined, string next)
	{
		int end = joined.Length;

		while (end > 0 && char.IsWhiteSpace(joined[end - 1]))
			end--;

		if ((end > 0 && joined[end - 1] == ',') || next.TrimStart().StartsWith(','))
			return true;

		// Right after the command's = means the only = of the joined text is its last character.
		if (end == 0 || joined[end - 1] != '=')
			return false;

		for (int i = 0; i < end - 1; i++)
		{
			if (joined[i] == '=')
				return false;
		}

		return true;
	}

	private bool Fail(SourceFileState file, int? lineNumber, string message, Exception? exception = null)
	{
		Error = new ScriptReadError(message, new SourceLocation(lineNumber, File: file.FilePath), exception);
		return false;
	}

	private bool TryLoadFileContent(SourceFileState currentFile)
	{
		string text;

		try
		{
			// A file included from two places is read twice, and its note shows once.
			text = TextInputReader.ReadAllText(
				path: currentFile.FilePath,
				overlay: _overlay,
				diagnostics: _run.Diagnostics,
				notedFiles: _filesWithEncodingNote
			);

			_run.RecordFileRead(currentFile.FilePath);
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			return Fail(file: currentFile, lineNumber: null, message: $"Cannot read file {currentFile.FilePath}: {ex.Reason()}", exception: ex);
		}

		// VB6 reads these files with Line Input (Module1.bas:5127, 5150, 5187, 13756-13760), which stops at a Ctrl-Z and drops what follows it.
		if (!TextFileUtilities.TryCutAtEndOfFileMark(text, out text, out int markLineNumber))
			return Fail(currentFile, markLineNumber, TextFileUtilities.TextAfterEndOfFileMarkError);

		currentFile.Lines = TextFileUtilities.SplitLines(text);
		_loadedFiles.Add((currentFile.FilePath, currentFile.Lines));

		currentFile.IsLoaded = true;
		return true;
	}

	/// <summary>
	/// Resolves the include file a raw <c>#INCLUDE</c> line names and pushes it onto the active source stack.
	/// </summary>
	/// <remarks>
	/// A failed read of one of two candidate files hands back the exception it caught, and every other failure
	/// hands back none. The method takes the line as the file holds it, with its comment, so that it can tell a quote
	/// that the comment cuts from one that is never closed.
	/// </remarks>
	internal bool TryPushInclude(
		string rawDirectiveLine,
		[NotNullWhen(false)] out string? errorMessage,
		out Exception? exception,
		CancellationToken token = default
	)
	{
		errorMessage = null;
		exception = null;

		token.ThrowIfCancellationRequested();

		// The next include overflows VB6's include stack (see MaxNestedIncludes), which the error handler of CompilaTutto reports as an
		// internal error (Module1.bas:8813, 8991-8994).
		if (_fileStack.Count > MaxNestedIncludes)
		{
			errorMessage = $"Too many nested #INCLUDE files, past the limit of {MaxNestedIncludes}; check for an include cycle";
			return false;
		}

		// VB6 has no such limit, and nested includes multiply their loads, so the port stops at a count of loads (PD-94).
		if (_includeCount >= MaxIncludeFiles)
		{
			errorMessage = $"Too many #INCLUDE files, past the limit of {MaxIncludeFiles} loads in one run; include fewer files";
			return false;
		}

		string includePath;

		string includeDirectiveLine = ScriptLineText.Normalize(rawDirectiveLine);

		int firstQuote = includeDirectiveLine.IndexOf('"');
		int closingQuote = firstQuote >= 0 ? includeDirectiveLine.IndexOf('"', firstQuote + 1) : -1;

		// VB6 fails an unclosed quote as "missing inverted commas" (Module1.bas:5019-5026, PrendiVirgolette in MioModulo.bas:3296); the port says what is wrong.
		if (firstQuote >= 0 && closingQuote < 0)
		{
			errorMessage = IsQuoteCutByComment(rawDirectiveLine)
				? "The #INCLUDE name holds a ';' or '//', where the line's comment starts and cuts the name, so an #INCLUDE name cannot hold either; rename the file"
				: "The #INCLUDE name opens a double quote that is never closed; close it or remove it";

			return false;
		}

		// VB6 includes the first quoted name and ignores the rest of the line (Module1.bas:5016-5018); the port fails on it (PD-37).
		if (closingQuote > firstQuote)
		{
			includePath = includeDirectiveLine.Substring(firstQuote + 1, closingQuote - firstQuote - 1);

			if (!string.IsNullOrWhiteSpace(includeDirectiveLine[(closingQuote + 1)..]))
			{
				errorMessage = $"The #INCLUDE line goes on after the quoted name \"{includePath}\"; write one #INCLUDE line per file";
				return false;
			}
		}
		else
		{
			string[] parts = includeDirectiveLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

			if (parts.Length < 2)
			{
				errorMessage = "Missing file name in #INCLUDE directive";
				return false;
			}

			if (parts.Length > 2)
			{
				errorMessage = $"The #INCLUDE file name '{string.Join(' ', parts[1..])}' holds a space; write the name in double quotes";
				return false;
			}

			includePath = parts[1];
		}

		// The two candidates take the casing of the files on disk (PD-29).
		string resolvedPath = _run.PathResolver.ResolveScriptPath(Path.GetDirectoryName(_fileStack[^1].FilePath) ?? string.Empty, includePath);

		// VB6 keeps the name after its last '\' or ':' and looks in the script folder (Module1.bas:5031, SoloNome in MioModulo.bas:805-818),
		// so a folder written with '/' stays part of the name (PD-16). The name holds no '\', and VB6 prefixes the folder to it as written.
		string vb6Path = _run.PathResolver.ResolveFullPath(Path.Join(_scriptDirectory, includePath[(includePath.LastIndexOfAny(['\\', ':']) + 1)..]));
		bool vb6PathExists = FileExistsOrInOverlay(vb6Path);

		if (!FileExistsOrInOverlay(resolvedPath))
		{
			if (!vb6PathExists)
			{
				errorMessage = $"The include file was not found: {includePath}";
				return false;
			}

			resolvedPath = vb6Path;
			_run.Logger.LogVerbose($"Found include file using VB6 fallback path: {resolvedPath}");
		}
		else if (vb6PathExists && !string.Equals(Path.GetFullPath(resolvedPath), Path.GetFullPath(vb6Path), StringComparison.OrdinalIgnoreCase))
		{
			if (
				!TryReadForComparison(path: resolvedPath, text: out string? first, out errorMessage, out exception)
				|| !TryReadForComparison(path: vb6Path, text: out string? second, out errorMessage, out exception)
			)
			{
				return false;
			}

			// Either file could be the one the author meant, so two that differ are an error rather than a silent choice.
			if (!string.Equals(first, second, StringComparison.Ordinal))
			{
				errorMessage =
					$"The #INCLUDE file '{includePath}' exists as {resolvedPath} and as {vb6Path}, and the two differ; delete one of them or make them the same";

				return false;
			}
		}

		_fileStack.Add(new SourceFileState(resolvedPath));
		_includeCount++;
		return true;
	}

	/// <summary>
	/// Returns whether the comment of a line cuts a quoted name, which closes its quote after the comment starts.
	/// </summary>
	/// <remarks>
	/// The caller has found one quote before the comment, so the first quote after the comment starts closes the name
	/// only when nothing but blanks or another comment follows it. Any other line stays an unclosed quote.
	/// </remarks>
	private static bool IsQuoteCutByComment(string rawLine)
	{
		int closingQuote = rawLine.IndexOf('"', ScriptLineText.WithoutComment(rawLine).Length);
		return closingQuote >= 0 && string.IsNullOrWhiteSpace(ScriptLineText.WithoutComment(rawLine[(closingQuote + 1)..]));
	}

	private bool TryReadForComparison(
		string path,
		[NotNullWhen(true)] out string? text,
		[NotNullWhen(false)] out string? errorMessage,
		out Exception? exception
	)
	{
		errorMessage = null;
		exception = null;

		try
		{
			text = VirtualFileSystem.ReadAllText(path, _overlay, out _);
			return true;
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			text = null;
			errorMessage = $"Cannot read file {path}: {ex.Reason()}";
			exception = ex;
			return false;
		}
	}

	private bool FileExistsOrInOverlay(string path) => VirtualFileSystem.FileExists(path, _overlay);

	private sealed class SourceFileState
	{
		/// <summary>Initializes the state of a file by its full path.</summary>
		/// <remarks>
		/// The full path names the file in diagnostics and scopes its defines, so two files of one name in different
		/// folders stay apart.
		/// </remarks>
		internal SourceFileState(string filePath) => FilePath = Path.GetFullPath(filePath);

		internal string FilePath { get; }

		internal string[] Lines { get; set; } = [];

		internal int LineIndex { get; set; }

		internal int LineNumber { get; set; }

		internal bool IsLoaded { get; set; }
	}
}
