using System.Diagnostics.CodeAnalysis;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Plugins;

/// <summary>The positional registry of <c>#DEFINE @PluginName id</c> lines that a trigger's plugin ID reads.</summary>
/// <remarks>
/// A define applies where it stands among the NG commands, so the registry holds the lines read so far
/// and a <c>#DEFINE @plugins clear</c> line empties it (<c>Module1.bas:13537-13633</c>).
/// </remarks>
internal sealed class PluginDefineRegistry
{
	private readonly CompilerLog _logger;
	private readonly DiagnosticSink _diagnostics;
	private readonly List<(string Name, int Id)> _defines = [];

	internal PluginDefineRegistry(CompilerLog logger, DiagnosticSink diagnostics)
	{
		_logger = logger;
		_diagnostics = diagnostics;
	}

	/// <summary>
	/// Empties the registry, as VB6 does before it compiles the first level section (<c>Module1.bas:8420</c>).
	/// </summary>
	internal void Clear() => _defines.Clear();

	/// <summary>Applies a <c>#DEFINE @PluginName id</c> or <c>#DEFINE @plugins clear</c> line.</summary>
	/// <remarks>
	/// VB6 stops at the first entry of the name, which takes the ID and warns; an entry before it with that ID
	/// is fatal (Module1.bas:13537-13633).
	/// </remarks>
	internal void Apply(NgLine line)
	{
		SourceLocation location = line.CommandLine.Location;
		string sourceText = line.CommandLine.SourceText;

		if (!ScriptLineText.TrySplitDefine(line.CommandLine.Text, out string[] parts, out string? countError))
		{
			_diagnostics.Error(
				countError,
				SourceLocation.ForTerm(line: location.Line, text: sourceText, term: ScriptLineText.DefineDirective, file: location.File)
			);

			return;
		}

		string name = parts[1];
		string value = parts[2];

		if (value.IgnoreCaseEquals("clear"))
		{
			if (!name.IgnoreCaseEquals("@plugins"))
			{
				int atColumn = sourceText.IndexOf('@');

				_diagnostics.Error(
					"Wrong syntax for clearing the plugin table; write #DEFINE @plugins clear",

					new SourceLocation(
						Line: location.Line,
						Column: Math.Max(atColumn, 0),
						Length: atColumn >= 0 ? sourceText.Length - atColumn : 0,
						File: location.File
					)
				);

				return;
			}

			_defines.Clear();
			_logger.LogVerbose("Cleared plugin definitions", indent: 1);
			return;
		}

		if (!name.IgnoreCaseStartsWith("@plugin"))
		{
			_diagnostics.Error(
				$"The line names @plugin but defines {name}; write #DEFINE @PluginName id to name the plugin of an exported trigger",
				SourceLocation.ForWord(line: location.Line, text: sourceText, wordIndex: 1, word: name, file: location.File)
			);

			return;
		}

		if (!PluginIdReader.TryRead(text: value, command: "#DEFINE @plugin", id: out int pluginId, out string? error))
		{
			_diagnostics.Error(error, SourceLocation.ForWord(line: location.Line, text: sourceText, wordIndex: 2, word: value, file: location.File));
			return;
		}

		string pluginName = name[1..];

		for (int i = 0; i < _defines.Count; i++)
		{
			if (_defines[i].Name.IgnoreCaseEquals(pluginName))
			{
				_diagnostics.Warning(
					$"Same plugin name [{pluginName}] had already been defined; add a [#DEFINE @plugins clear] line to clear the previous definition",
					SourceLocation.ForWord(line: location.Line, text: sourceText, wordIndex: 1, word: name, file: location.File)
				);

				_defines[i] = (pluginName, pluginId);
				_logger.LogVerbose($"#DEFINE @{pluginName} = {pluginId}", indent: 1);
				return;
			}

			if (_defines[i].Id == pluginId)
			{
				_diagnostics.Error(
					FormattableString.Invariant(
						$"The plugin ID {pluginId} is already defined for another plugin; add a [#DEFINE @plugins clear] line to clear the previous definitions"
					),

					SourceLocation.ForWord(line: location.Line, text: sourceText, wordIndex: 2, word: value, file: location.File)
				);

				return;
			}
		}

		_defines.Add((pluginName, pluginId));
		_logger.LogVerbose($"#DEFINE @{pluginName} = {pluginId}", indent: 1);
	}

	/// <summary>Finds the name of the first entry that has a plugin ID.</summary>
	internal bool TryGetName(int pluginId, [NotNullWhen(true)] out string? pluginName)
	{
		foreach ((string name, int id) in _defines)
		{
			if (id == pluginId)
			{
				pluginName = name;
				return true;
			}
		}

		pluginName = null;
		return false;
	}
}
