using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Plugins;

namespace TRNGScriptCompiler.Constants;

/// <summary>
/// Resolves an expression name to a <c>#DEFINE</c>, a TRNG constant, a plugin constant, a slot or a static,
/// in that order.
/// </summary>
/// <remarks>
/// VB6 puts plugin constants before slots and statics (Module1.bas:4248-4269, :10116-10170); a #DEFINE shares no name,
/// so may go first (PD-35).
/// </remarks>
internal sealed class ConstantResolver
{
	private readonly DefineRegistry _defines;
	private readonly ConstantCatalog _catalog;
	private readonly PluginRegistry _plugins;

	/// <remarks>
	/// The <c>Plugin=</c> lines of <paramref name="plugins"/> give a plugin constant its plugin ID.
	/// </remarks>
	internal ConstantResolver(DefineRegistry defines, ConstantCatalog catalog, PluginRegistry plugins)
	{
		_defines = defines;
		_catalog = catalog;
		_plugins = plugins;
	}

	/// <summary>
	/// Resolves a name against the <c>#DEFINE</c> values of a file, or of none when the file is <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// The name matches without regard to case. A failed <c>#DEFINE</c> or a plugin constant the script cannot use
	/// fails with an error that quotes the name in upper case, and an unknown name returns the default.
	/// </remarks>
	internal ConstantResolutionResult Resolve(string name, string? file)
	{
		if (file is not null && _defines.TryGet(file, name, out DefineRecord? define))
			return new(Success: true, PluginId: 0, Value: define.Value, Kind: ConstantKind.Define);

		if (_catalog.TryGet(name, out int value))
			return new(Success: true, PluginId: 0, value, Kind: ConstantKind.Constant);

		IReadOnlyList<PluginConstant> pluginConstants = _catalog.GetPluginConstants(name);

		if (pluginConstants.Count > 0)
			return ResolvePluginConstant(name, pluginConstants);

		if (_catalog.ObjectsH.TryGetSlotConstant(name, out value))
			return new(Success: true, PluginId: 0, value, Kind: ConstantKind.Slot);

		if (_catalog.ObjectsH.TryGetStaticConstant(name, out value))
			return new(Success: true, PluginId: 0, value, Kind: ConstantKind.Static);

		// The failed #DEFINE line reports its own error, so a use names that line rather than calling the name unknown. VB6 looks the
		// constants up before the defines (Module1.bas:10117-10170), so a failed define of a constant's name leaves the constant's value in use.
		if (file is not null && _defines.TryGetInvalid(file, name, out int defineLine))
		{
			return new(
				Success: false,
				PluginId: 0,
				Value: 0,
				Kind: ConstantKind.Define,
				Error: $"{name.ToUpperInvariant()} names the #DEFINE of line {defineLine}, which gives no value; fix that line"
			);
		}

		return default;
	}

	/// <summary>
	/// Resolves a plugin constant, failing when two plugins define the name or no <c>Plugin=</c> line declares
	/// its plugin (PD-61).
	/// </summary>
	/// <remarks>
	/// Two plugins give a name two IDs even with one value, so a use is unclear (PD-62);
	/// VB6 renames one after a MsgBox (Module1.bas:4276-4318).
	/// </remarks>
	private ConstantResolutionResult ResolvePluginConstant(string name, IReadOnlyList<PluginConstant> pluginConstants)
	{
		if (pluginConstants.Count > 1)
		{
			string sources = string.Join(
				" and ",
				pluginConstants.Select(constant => FormattableString.Invariant($"{constant.Plugin} ({constant.Value})"))
			);

			return new(
				Success: false,
				PluginId: 0,
				Value: 0,
				Kind: ConstantKind.PluginConstant,
				Error: $"{name.ToUpperInvariant()} is defined by the plugins {sources}, so it does not say which plugin it means; rename the constant in all but one of the plugin scripts"
			);
		}

		PluginConstant constant = pluginConstants[0];

		if (!_plugins.TryGetDeclaredId(constant.Plugin, out int pluginId))
		{
			return new(
				Success: false,
				PluginId: 0,
				Value: 0,
				Kind: ConstantKind.PluginConstant,
				Error: $"{name.ToUpperInvariant()} is a constant of the plugin {constant.Plugin}, which no Plugin= line declares; add a Plugin= line for {constant.Plugin} to [Options]"
			);
		}

		return new(Success: true, pluginId, Value: constant.Value, Kind: ConstantKind.PluginConstant);
	}
}
