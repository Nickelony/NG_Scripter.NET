using System.Diagnostics.CodeAnalysis;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Plugins;

/// <summary>
/// Holds a run's plugins: scripts, the <c>Plugin=</c> lines that declare them and the positional <c>@plugin</c>
/// define registry.
/// </summary>
/// <remarks>
/// As in VB6 ConvertiIdPluginTrigger (Module1.bas:6969-7012), the <c>#DEFINE @PluginName id</c> a trigger names maps
/// to its <c>Plugin=</c> ID.
/// </remarks>
internal sealed class PluginRegistry
{
	private readonly IReadOnlyList<PluginScript> _scripts;
	private readonly string _engineDirectory;
	private readonly PathResolver _paths;
	private readonly CompilerLog _logger;
	private readonly DiagnosticSink _diagnostics;
	private readonly List<(PluginInfo Plugin, SourceLocation Location)> _declarations = [];
	private readonly HashSet<(string? File, int? Line)> _rejectedLines = [];

	/// <remarks>
	/// The engine folder holds the <c>.dll</c> of an installed plugin, which <paramref name="paths"/> finds,
	/// and the log gets a verbose note for each translated ID.
	/// </remarks>
	internal PluginRegistry(
		IReadOnlyList<PluginScript> scripts,
		string engineDirectory,
		PathResolver paths,
		CompilerLog logger,
		DiagnosticSink diagnostics
	)
	{
		_scripts = scripts;
		Defines = new PluginDefineRegistry(logger, diagnostics);
		_engineDirectory = engineDirectory;
		_paths = paths;
		_logger = logger;
		_diagnostics = diagnostics;
	}

	/// <summary>Gets the registry of the <c>#DEFINE @PluginName id</c> lines read so far.</summary>
	internal PluginDefineRegistry Defines { get; }

	/// <summary>Gets the plugins the script declares, in the order of their <c>Plugin=</c> lines.</summary>
	/// <remarks>
	/// A test seam: the registry reads its declarations itself, and the tests read them here.
	/// </remarks>
	internal IReadOnlyList<PluginInfo> Declarations => [.. _declarations.Select(declaration => declaration.Plugin)];

	/// <summary>
	/// Records a <c>Plugin=</c> declaration, skipping a line of fewer than two arguments, which the NG compile reports.
	/// </summary>
	/// <remarks>
	/// VB6 reads each <c>Plugin=</c> line anywhere in <c>script.txt</c> by GetCampoNumero (Module1.bas:15024-15104);
	/// includes count too (PD-87).
	/// </remarks>
	internal void Declare(NgLine line)
	{
		IReadOnlyList<string> arguments = line.CommandLine.Arguments;

		if (arguments.Count < 2 || arguments[1].Trim().Length == 0)
			return;

		SourceLocation location = line.CommandLine.Location;

		string idText = arguments[0].Trim();
		string name = arguments[1].Trim();

		if (!PluginIdReader.TryRead(text: idText, command: "Plugin=", out int id, error: out string? idError))
		{
			_diagnostics.Error(idError, line.CommandLine.ArgumentLocation(0));
			_rejectedLines.Add((location.File, location.Line));
			return;
		}

		foreach ((PluginInfo declared, SourceLocation declaredAt) in _declarations)
		{
			if (!declared.Name.IgnoreCaseEquals(name))
				continue;

			// VB6 writes both lines, and its trigger table takes the first ID (Module1.bas:6993-6995); the port fails (PD-57).
			if (declared.Id != id)
			{
				_diagnostics.Error(
					FormattableString.Invariant(
						$"Plugin= declares {name} with ID {id}, but {SourceLocation.DescribeLine(declaredAt, location.File)} declares it with ID {declared.Id}, and a plugin takes one ID; keep one of the two lines"
					),

					line.CommandLine.ArgumentLocation(1)
				);
			}

			return;
		}

		PluginScript? script = _scripts.FirstOrDefault(candidate => candidate.Name.IgnoreCaseEquals(name));

		if (script is null && !File.Exists(_paths.ResolveScriptPath(_engineDirectory, $"{name}.dll")))
			_diagnostics.Warning($"The plugin '{name}' was not found, because {name}.dll is missing", location);

		_declarations.Add((new PluginInfo { Name = name, Id = id }, location));
		_logger.LogVerbose($"Registered plugin: {name} with ID {id}");
	}

	/// <summary>
	/// Gets a value indicating whether <see cref="Declare"/> rejected the ID of the <c>Plugin=</c> line at a location.
	/// </summary>
	/// <returns><see langword="true"/> when the line's ID was reported as no number.</returns>
	/// <remarks>
	/// The NG compile passes over such a line, so its ID is reported once.
	/// </remarks>
	internal bool IsRejected(SourceLocation location) => _rejectedLines.Contains((location.File, location.Line));

	/// <summary>
	/// Finds the ID of the <c>Plugin=</c> line that declares a plugin, matching its name without regard to case.
	/// </summary>
	internal bool TryGetDeclaredId(string pluginName, out int pluginId)
	{
		foreach ((PluginInfo declared, _) in _declarations)
		{
			if (declared.Name.IgnoreCaseEquals(pluginName))
			{
				pluginId = declared.Id;
				return true;
			}
		}

		pluginId = 0;
		return false;
	}

	/// <summary>
	/// Translates the plugin ID of an exported trigger into the ID of its plugin's <c>Plugin=</c> line.
	/// </summary>
	/// <returns>
	/// <see langword="false"/> for an undeclared plugin, named in <paramref name="undeclaredPlugin"/>.
	/// </returns>
	/// <remarks>
	/// An ID no <c>@plugin</c> define names stays as written. VB6 fails an undeclared plugin (Module1.bas:6969-7012),
	/// and the caller reports one error for each such plugin.
	/// </remarks>
	internal bool TryTranslateTriggerPluginId(int defineId, out int pluginId, [NotNullWhen(false)] out string? undeclaredPlugin)
	{
		pluginId = defineId;
		undeclaredPlugin = null;

		if (!Defines.TryGetName(defineId, out string? pluginName))
			return true;

		if (!TryGetDeclaredId(pluginName, out int declaredId))
		{
			undeclaredPlugin = pluginName;
			return false;
		}

		if (declaredId != defineId)
			_logger.LogVerbose($"Converted TriggerGroup plugin ID: {defineId} ({pluginName}) -> {declaredId}", indent: 2);

		pluginId = declaredId;
		return true;
	}
}
