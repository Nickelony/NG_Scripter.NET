using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Plugins;

namespace TRNGScriptCompiler.Constants;

/// <summary>
/// Holds the TRNG constants, the <c>Objects.h</c> names and the plugin constants, which a script uses
/// without defining them.
/// </summary>
/// <remarks>
/// VB6 merges plugin constants at startup (Module1.bas:4248-4269); each keeps its plugin here, and a name two plugins
/// define fails only where used.
/// </remarks>
internal sealed class ConstantCatalog
{
	private readonly IReadOnlyDictionary<string, ConstantDefinition> _constants;
	private readonly Dictionary<string, List<PluginConstant>> _pluginConstants = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Initializes a new catalog and reads the constants of the constants file and of each plugin script.
	/// </summary>
	/// <remarks>
	/// The log gets a verbose line per constants-file constant. The sink gets each file's grammar diagnostics,
	/// and a warning per plugin constant whose value differs from the TRNG constant of its name.
	/// </remarks>
	internal ConstantCatalog(
		ConstantsFile constantsFile,
		ObjectsHCatalog objectsH,
		IReadOnlyList<PluginScript> pluginScripts,
		CompilerLog logger,
		DiagnosticSink diagnostics
	)
	{
		ObjectsH = objectsH;

		logger.LogVerbose($"Loading constants from: {constantsFile.FilePath}");
		_constants = ConstantsFileParser.Parse(constantsFile.Lines, constantsFile.FilePath, diagnostics);

		foreach ((string name, ConstantDefinition definition) in _constants)
			logger.LogVerbose($"{name} = {definition.Value}", indent: 1);

		logger.Log($"Loaded {_constants.Count} global constants");

		foreach (PluginScript script in pluginScripts)
			AddPluginConstants(script, logger, diagnostics);
	}

	/// <summary>Gets the <c>Objects.h</c> table, which holds the slot and static names.</summary>
	internal ObjectsHCatalog ObjectsH { get; }

	/// <summary>Finds a constant of the constants file, matching its name without regard to case.</summary>
	internal bool TryGet(string name, out int value)
	{
		bool found = _constants.TryGetValue(name, out ConstantDefinition definition);
		value = definition.Value;

		return found;
	}

	/// <summary>
	/// Finds the plugin constants of a name, matched without regard to case, one per plugin that defines it.
	/// </summary>
	/// <returns>The constants, in plugin-name order; empty when no plugin defines the name.</returns>
	internal IReadOnlyList<PluginConstant> GetPluginConstants(string name) =>
		_pluginConstants.TryGetValue(name, out List<PluginConstant>? constants) ? constants : [];

	private void AddPluginConstants(PluginScript script, CompilerLog logger, DiagnosticSink diagnostics)
	{
		logger.LogVerbose($"Loading plugin constants: {script.Name}");

		IReadOnlyDictionary<string, ConstantDefinition> constants = ConstantsFileParser.Parse(script.Lines, script.Path, diagnostics);

		foreach ((string name, (int value, int line)) in constants)
		{
			// The TRNG constant keeps winning, as in VB6, whose startup offers to rename the plugin's copy (Module1.bas:4276-4318) (PD-62).
			if (TryGet(name, out int builtIn) && builtIn != value)
			{
				diagnostics.Warning(
					FormattableString.Invariant(
						$"The plugin constant {name} is {value}, but the TRNG constant {name} is {builtIn}, which a script that names it gets; rename the constant in the plugin script to use the plugin's value"
					),

					new SourceLocation(line, File: script.Path)
				);
			}

			if (!_pluginConstants.TryGetValue(name, out List<PluginConstant>? entries))
			{
				entries = [];
				_pluginConstants.Add(name, entries);
			}

			entries.Add(new PluginConstant(script.Name, value));
		}

		logger.LogVerbose($"Loaded {constants.Count} constant(s) from {script.Name}");
	}
}
