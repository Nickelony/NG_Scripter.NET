using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Constants;

/// <summary>Records the <c>#DEFINE</c> values of a file in the run's define registry.</summary>
/// <remarks>
/// A <c>#DEFINE @plugin</c> line applies where it stands, so the NG compile reads it and this recorder passes over it.
/// </remarks>
internal sealed class DefineRecorder
{
	private readonly CompilationRun _run;
	private readonly ExpressionEvaluator _expressionEvaluator;

	internal DefineRecorder(CompilationRun run, ExpressionEvaluator expressionEvaluator)
	{
		_run = run;
		_expressionEvaluator = expressionEvaluator;
	}

	/// <summary>Scans all lines of a file and records the <c>#DEFINE</c> values they hold.</summary>
	/// <param name="filePath">The full path of the file, which scopes its defines.</param>
	/// <param name="lines">The raw file lines.</param>
	/// <remarks>
	/// A failed define reports to the run's diagnostics, and the scan goes on with the next line.
	/// </remarks>
	internal void Scan(string filePath, IReadOnlyList<string> lines)
	{
		for (int index = 0; index < lines.Count; index++)
		{
			string rawLine = lines[index];

			// Normalizing drops a comment and whitespace only, so a line whose normalized text opens with #DEFINE opens with it as written.
			if (!rawLine.AsSpan().TrimStart().StartsWith(ScriptLineText.DefineDirective, StringComparison.OrdinalIgnoreCase))
				continue;

			string line = ScriptLineText.Normalize(rawLine);

			if (!ScriptLineText.StartsWithDirective(line, ScriptLineText.DefineDirective) || NgLine.IsPluginDefineText(line))
				continue;

			Record(file: filePath, line, lineNumber: index + 1, originalLine: ScriptLineText.WithoutComment(rawLine));
		}
	}

	/// <remarks>
	/// The line is normalized and the original cut before its comment, and the file is the full path that scopes
	/// the define.
	/// </remarks>
	private void Record(string file, string line, int lineNumber, string originalLine)
	{
		if (!ScriptLineText.TrySplitDefine(line, out string[] parts, out string? countError))
		{
			_run.Diagnostics.Error(
				countError,
				SourceLocation.ForTerm(line: lineNumber, text: originalLine, term: ScriptLineText.DefineDirective, file)
			);

			if (parts.Length > 1)
				_run.Defines.AddInvalid(file, parts[1], lineNumber);

			return;
		}

		string name = parts[1];
		string valueOrCommand = parts[2];

		// VB6 reads the value with ConvertiStringaNumerica, 4 bytes wide (Module1.bas:13683).
		NumericEvaluation evaluation = _expressionEvaluator.Evaluate(valueOrCommand, file);

		if (!evaluation.Success)
		{
			_run.Diagnostics.Error(
				$"The #DEFINE value [{valueOrCommand}] is invalid: {evaluation.Error}",
				SourceLocation.ForWord(line: lineNumber, text: originalLine, wordIndex: 2, word: valueOrCommand, file)
			);

			_run.Defines.AddInvalid(file, name, lineNumber);

			return;
		}

		int value = evaluation.Value;

		// VB6 looks up constants, plugin constants and slots before defines, and statics after them (Module1.bas:10117-10170), so such a
		// define changes nothing or hides a static without a word; the port fails (PD-35).
		ConstantResolutionResult existingName = _expressionEvaluator.ResolveName(name, file);

		if (existingName.IsKnown && existingName.Kind != ConstantKind.Define)
		{
			_run.Diagnostics.Error(
				$"#DEFINE {name} reuses the name of {existingName.Kind.Describe()}, and a name holds one value; rename the define",
				SourceLocation.ForWord(line: lineNumber, text: originalLine, wordIndex: 1, word: name, file)
			);

			return;
		}

		if (_run.Defines.TryGet(file, name, out DefineRecord? existingDefine))
		{
			// VB6 keeps the first value with a warning (Module1.bas:13691-13698), so the script does not say which value it means (PD-31).
			if (existingDefine.Value != value)
			{
				_run.Diagnostics.Error(
					$"#DEFINE {name} gives a value different from line {existingDefine.Location.Line}, and a define holds one value; keep one of the two lines",
					SourceLocation.ForWord(line: lineNumber, text: originalLine, wordIndex: 1, word: name, file)
				);
			}
			else
			{
				_run.Diagnostics.Warning(
					$"#DEFINE {name} repeats line {existingDefine.Location.Line} with the same value; remove one of the two lines",
					SourceLocation.ForWord(line: lineNumber, text: originalLine, wordIndex: 1, word: name, file)
				);
			}
		}
		else
		{
			_run.Defines.Add(
				new DefineRecord
				{
					Name = name,
					Value = value,
					Location = new SourceLocation(lineNumber, File: file),
				}
			);

			_run.Logger.LogVerbose($"#DEFINE {name} = {value}", indent: 1);
		}
	}
}
