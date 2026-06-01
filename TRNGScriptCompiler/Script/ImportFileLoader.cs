using System.Diagnostics.CodeAnalysis;
using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Validates <c>ImportFile=</c> commands and loads each file from disk into an <see cref="NgImportFile"/>.
/// </summary>
internal sealed class ImportFileLoader
{
	private readonly CompilationRun _run;
	private readonly ExpressionEvaluator _expressionEvaluator;
	private readonly string _engineDirectoryPath;
	private readonly List<NgImportFile> _importFiles;
	private readonly Dictionary<short, (int Id, string Path)> _declaredIds = [];

	private const string ImportFileCommand = "ImportFile=";

	/// <remarks>
	/// The engine folder is where an <c>ImportFile=</c> path is first looked up. The loader adds each import file
	/// it reads to <paramref name="importFiles"/>.
	/// </remarks>
	internal ImportFileLoader(CompilationRun run, ExpressionEvaluator expressionEvaluator, string engineDirectoryPath, List<NgImportFile> importFiles)
	{
		_run = run;
		_expressionEvaluator = expressionEvaluator;
		_engineDirectoryPath = engineDirectoryPath;
		_importFiles = importFiles;
	}

	/// <summary>
	/// Validates and loads an Options <c>ImportFile=</c> command line, whose location holds the 1-based line
	/// and the full path of its script.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when the import file was loaded; otherwise, <see langword="false"/>, after an error.
	/// </returns>
	internal bool TryLoad(ScriptCommandLine line)
	{
		IReadOnlyList<string> arguments = line.Arguments;
		SourceLocation location = line.Location;

		// VB6 reads ImportFile= as an NG command of four arguments, Word:String:Word:Word (Module1.bas:7071-7106).
		if (arguments.Count != 4)
		{
			_run.Diagnostics.Error(ArgumentCountMessage.Wrong(ImportFileCommand, "4 (ID, Path, Type, Mode)", arguments.Count), line.CommandLocation);
			return false;
		}

		if (
			!TryEvaluateArgument(line, index: 0, label: "Import ID", value: out int importId)
			|| !TryCheckWord(line, index: 0, value: importId, field: "ID")
		)
		{
			return false;
		}

		// VB6 checks the IDs of every [Options] section as one list (Module1.bas:8306, AzzeraDuplicati at :1212), and an IMPORT
		// argument names its import by ID. The chunk writes the ID as a word, so -1 and 65535 are one ID (PD-95).
		if (_declaredIds.TryGetValue((short)importId, out (int Id, string Path) first))
		{
			_run.Diagnostics.Error(DuplicateIdMessage(importId, first.Id, first.Path), line.ArgumentLocation(0));
			return false;
		}

		string filePath = arguments[1].Trim();

		if (!TryResolveImportPath(line, filePath, out string? fullPath))
			return false;

		if (
			!TryEvaluateArgument(line, index: 2, label: "File Type", value: out int fileType)
			|| !TryCheckWord(line, index: 2, value: fileType, field: "file type")
		)
		{
			return false;
		}

		if (!TryEvaluateArgument(line, index: 3, label: "Import Mode", value: out int importModeVal))
			return false;

		// VB6 logs the mode through a three-entry name array, which fails outside 0-2 (Module1.bas:8282, 8600-8602).
		if (importModeVal is < 0 or > 2)
		{
			_run.Diagnostics.Error(
				FormattableString.Invariant($"The ImportFile= mode {importModeVal} lies outside 0 to 2; write 0, IMPORT_MEMORY or IMPORT_TEMPORARY"),
				line.ArgumentLocation(3)
			);

			return false;
		}

		var importMode = (NgImportMode)importModeVal;

		byte[] fileData;

		try
		{
			fileData = InputFileReader.ReadAllBytes(fullPath);
			_run.RecordFileRead(fullPath);
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			_run.Diagnostics.Error($"Cannot read import file {Path.GetFileName(fullPath)}: {ex.Reason()}", location, ex);
			return false;
		}

		var importFile = new NgImportFile
		{
			ImportId = importId,
			FileName = filePath,
			ImportMode = importMode,
			FileType = fileType,
			Data = fileData,
			Location = location,
		};

		_importFiles.Add(importFile);
		_declaredIds.Add((short)importId, (importId, filePath));

		_run.Logger.LogVerbose($"Loaded ImportFile: {importFile.FileName} (ID: {importId}, Size: {fileData.Length})");
		return true;
	}

	/// <summary>
	/// Words the error for an ID whose word an earlier <c>ImportFile=</c> declared, naming both IDs as written.
	/// </summary>
	private static string DuplicateIdMessage(int id, int firstId, string firstPath)
	{
		if (id == firstId)
			return FormattableString.Invariant($"ImportFile= ID {id} is already declared for {firstPath}; give each ImportFile= its own ID");

		return FormattableString.Invariant(
			$"ImportFile= ID {id} is the same word as ID {firstId}, already declared for {firstPath}; give each ImportFile= its own ID"
		);
	}

	/// <summary>
	/// Checks an <c>ImportFile=</c> path and finds its file, under the engine folder or else beside the script file.
	/// </summary>
	private bool TryResolveImportPath(ScriptCommandLine line, string filePath, [NotNullWhen(true)] out string? fullPath)
	{
		SourceLocation location = line.Location;
		fullPath = null;

		// VB6 prefixes the engine folder to the path as written (Module1.bas:7635), so a path with a drive or a share never resolves.
		if (Path.IsPathFullyQualified(filePath) || filePath.Contains(':'))
		{
			_run.Diagnostics.Error(
				$"The ImportFile= path '{filePath}' is absolute, but an import path is relative to the engine folder; write it relative to {_engineDirectoryPath}",
				line.ArgumentLocation(1)
			);

			return false;
		}

		// The name field holds one byte for each character of the path (Windows-1252) and a terminator, and VB6 cuts a longer name with no
		// terminator (Module1.bas:7645).
		if (filePath.Length >= NgImportFile.NameFieldBytes)
		{
			_run.Diagnostics.Error(
				$"The ImportFile= path '{filePath}' is {filePath.Length} characters long, but its field holds {NgImportFile.NameFieldBytes - 1} and a terminator; move the file to a shorter path",
				line.ArgumentLocation(1)
			);

			return false;
		}

		// An import path is relative to the engine folder (Module1.bas:7635-7640); the folder of the script file is the fallback (PD-83). Either
		// takes the casing of the file on disk (PD-29). VB6 prefixes the folder to the path as written, so a leading '\', as in \data.bin,
		// joins the path to the folder rather than rooting it.
		string relativePath = filePath.TrimStart('\\', '/');
		string candidate = _run.PathResolver.ResolveScriptPath(_engineDirectoryPath, relativePath);

		if (!File.Exists(candidate))
			candidate = _run.PathResolver.ResolveScriptPath(Path.GetDirectoryName(location.File) ?? string.Empty, relativePath);

		if (!File.Exists(candidate))
		{
			_run.Diagnostics.Error($"The import file was not found: {filePath}", line.CommandLocation);
			return false;
		}

		fullPath = candidate;
		return true;
	}

	/// <summary>
	/// Evaluates an <c>ImportFile=</c> argument, reporting a failure as an invalid value of its label.
	/// </summary>
	private bool TryEvaluateArgument(ScriptCommandLine line, int index, string label, out int value)
	{
		NumericEvaluation evaluation = _expressionEvaluator.Evaluate(line.Arguments[index], line.Location.File);
		value = evaluation.Value;

		if (evaluation.Success)
			return true;

		_run.Diagnostics.Error($"Invalid {label}: {evaluation.Error}", line.ArgumentLocation(index));
		return false;
	}

	/// <summary>
	/// Width-checks a Word argument as VB6 does (<c>Module1.bas:4915-4937</c>), accepting its signed and unsigned
	/// reading (PD-56).
	/// </summary>
	private bool TryCheckWord(ScriptCommandLine line, int index, int value, string field)
	{
		if (FieldRange.Fits(value, widthInBytes: 2, out long minimum, out long maximum))
			return true;

		_run.Diagnostics.Error(
			$"The ImportFile= {field} {FieldRange.DescribeOutside(value, minimum, maximum, holder: "its word")}",
			line.ArgumentLocation(index)
		);

		return false;
	}
}
