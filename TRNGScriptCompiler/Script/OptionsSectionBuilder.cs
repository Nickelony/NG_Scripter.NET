using System.Globalization;
using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Collects the classic options, the <c>ImportFile=</c> files and the NG lines of every <c>[Options]</c> section
/// of a script.
/// </summary>
/// <remarks>
/// Every <c>[Options]</c> section adds to one set, as VB6 does (<c>Module1.bas:5448-5456</c>).
/// </remarks>
internal sealed class OptionsSectionBuilder
{
	private readonly DiagnosticSink _diagnostics;
	private readonly ExpressionEvaluator _evaluator;
	private readonly IReadOnlySet<string> _ngCommandNames;
	private readonly ImportFileLoader _importFileLoader;
	private readonly SettingRecorder _settings;
	private readonly List<NgImportFile> _importFiles = [];
	private readonly List<NgLine> _ngLines = [];

	private ScriptMainFlags _flags;

	private int _inputTimeOut;
	private int _security;

	/// <remarks>
	/// The NG command names carry their <c>=</c>, and an <c>ImportFile=</c> path resolves in the engine folder.
	/// </remarks>
	internal OptionsSectionBuilder(CompilationRun run, ExpressionEvaluator evaluator, IReadOnlySet<string> ngCommandNames, string engineDirectoryPath)
	{
		_diagnostics = run.Diagnostics;
		_evaluator = evaluator;
		_ngCommandNames = ngCommandNames;
		_settings = new SettingRecorder(run.Diagnostics);
		_importFileLoader = new ImportFileLoader(run, expressionEvaluator: evaluator, engineDirectoryPath, importFiles: _importFiles);
	}

	/// <summary>Gets the classic options the lines set.</summary>
	internal ScriptOptions Options =>
		new()
		{
			Flags = _flags,
			InputTimeOut = _inputTimeOut,
			Security = _security,
		};

	/// <summary>Gets the files the <c>ImportFile=</c> lines read.</summary>
	internal IReadOnlyList<NgImportFile> ImportFiles => _importFiles;

	/// <summary>Gets the NG lines in source order.</summary>
	internal IReadOnlyList<NgLine> NgLines => _ngLines;

	/// <summary>
	/// Adds a command line of an <c>[Options]</c> section, returning <see langword="false"/> after an error
	/// for an invalid line.
	/// </summary>
	/// <remarks>
	/// Classic options win over a same-named NG command, as in VB6's <c>Select Case</c>, and any other command fails
	/// (<c>Module1.bas:5401-5463</c>).
	/// </remarks>
	internal bool TryAdd(ScriptCommandLine line)
	{
		string command = line.Command;

		if (command.IgnoreCaseEquals("ImportFile="))
			return _importFileLoader.TryLoad(line);

		if (command.IgnoreCaseEquals("LoadSave="))
			return TrySetFlag(line, ScriptMainFlags.LoadSave);

		if (command.IgnoreCaseEquals("Title="))
			return TrySetFlag(line, ScriptMainFlags.Title);

		if (command.IgnoreCaseEquals("PlayAnyLevel="))
			return TrySetFlag(line, ScriptMainFlags.PlayAnyLevel);

		if (command.IgnoreCaseEquals("FlyCheat="))
			return TrySetFlag(line, ScriptMainFlags.FlyCheat);

		if (command.IgnoreCaseEquals("DemoDisc="))
			return TrySetFlag(line, ScriptMainFlags.DemoDisc);

		if (command.IgnoreCaseEquals("InputTimeOut="))
			return TrySetInputTimeOut(line);

		if (command.IgnoreCaseEquals("Security="))
			return TrySetSecurity(line);

		if (_ngCommandNames.Contains(command))
		{
			_ngLines.Add(new NgLine(line));
			return true;
		}

		_diagnostics.Error(ScriptCommandMessages.UnknownCommand(ScriptSectionHeaders.Of(ScriptSectionType.Options), command), line.CommandLocation);
		return false;
	}

	private bool TrySetFlag(ScriptCommandLine line, ScriptMainFlags flag)
	{
		if (!TryReadEnabledState(line, out bool enabled) || !_settings.TryRecord(key: flag.ToString(), value: enabled.ToString(), line))
			return false;

		if (enabled)
			_flags |= flag;

		return true;
	}

	private bool TryReadEnabledState(ScriptCommandLine line, out bool enabled)
	{
		enabled = false;

		if (
			!ScriptArgumentResolver.CheckArgumentCount(
				diagnostics: _diagnostics,
				args: line.Arguments,
				expected: 1,
				command: line.Command,
				location: line.CommandLocation
			)
		)
		{
			return false;
		}

		if (EnabledState.TryParse(line.Arguments[0], out enabled))
			return true;

		_diagnostics.Error(EnabledState.InvalidValueError(1, line.Command, line.Arguments[0]), line.CommandLocation);
		return false;
	}

	private bool TrySetInputTimeOut(ScriptCommandLine line)
	{
		if (
			!TryEvaluateSingleArgument(line, out int timeout)
			|| !_settings.TryRecord(key: "InputTimeOut=", value: timeout.ToString(CultureInfo.InvariantCulture), line)
		)
		{
			return false;
		}

		_inputTimeOut = timeout;
		return true;
	}

	private bool TrySetSecurity(ScriptCommandLine line)
	{
		if (!TryEvaluateSingleArgument(line, out int security))
			return false;

		// Script.dat stores Security= in a VB6 Byte, which overflows outside 0-255 (Module1.bas:5430, 6671).
		if (security is < 0 or > 255)
		{
			_diagnostics.Error(
				FormattableString.Invariant($"The value of '{line.Command}' must be between 0 and 255: {security}"),
				line.CommandLocation
			);

			return false;
		}

		if (!_settings.TryRecord(key: "Security=", value: security.ToString(CultureInfo.InvariantCulture), line))
			return false;

		_security = security;
		return true;
	}

	/// <summary>Reads the one numeric argument of <c>InputTimeOut=</c> or <c>Security=</c>.</summary>
	/// <remarks>
	/// VB6 aborts when the first argument does not parse (<c>Module1.bas:5426-5430</c>); extra arguments,
	/// which VB6 ignores, fail too (PD-57).
	/// </remarks>
	private bool TryEvaluateSingleArgument(ScriptCommandLine line, out int value)
	{
		value = 0;

		if (
			!ScriptArgumentResolver.CheckArgumentCount(
				diagnostics: _diagnostics,
				args: line.Arguments,
				expected: 1,
				command: line.Command,
				location: line.CommandLocation
			)
		)
		{
			return false;
		}

		NumericEvaluation evaluation = _evaluator.Evaluate(line.Arguments[0], line.Location.File);
		value = evaluation.Value;

		if (evaluation.Success)
			return true;

		_diagnostics.Error($"The value of '{line.Command}' is invalid: {evaluation.Error}", line.CommandLocation);
		return false;
	}
}
