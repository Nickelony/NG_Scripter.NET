using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Records each one-value setting's first value, failing a later line with another value and warning on an identical
/// one (PD-31).
/// </summary>
/// <remarks>
/// VB6 keeps the last number or extension (<c>Module1.bas:5426-5430</c>, <c>5545-5556</c>) and only ever sets a flag
/// (<c>:5402-5425</c>).
/// </remarks>
internal sealed class SettingRecorder
{
	private readonly DiagnosticSink _diagnostics;
	private readonly Dictionary<string, (string Value, SourceLocation Location)> _values = new(StringComparer.OrdinalIgnoreCase);

	/// <param name="diagnostics">The sink that receives the error or the warning for a repeat.</param>
	internal SettingRecorder(DiagnosticSink diagnostics) => _diagnostics = diagnostics;

	/// <summary>Records the value a line gives a setting, whose key is unique among this recorder's settings.</summary>
	/// <returns>
	/// <see langword="false"/> when an earlier line gave the setting
	/// a different value; otherwise, <see langword="true"/>.
	/// </returns>
	internal bool TryRecord(string key, string value, ScriptCommandLine line)
	{
		if (!_values.TryGetValue(key, out (string Value, SourceLocation Location) earlier))
		{
			_values[key] = (value, line.Location);
			return true;
		}

		if (!string.Equals(earlier.Value, value, StringComparison.Ordinal))
		{
			_diagnostics.Error(
				RepeatedSettingMessages.DifferentValue(subject: line.Command, earlier.Location, currentFile: line.Location.File),
				line.CommandLocation
			);

			return false;
		}

		_diagnostics.Warning(
			RepeatedSettingMessages.SameValue(subject: line.Command, earlier.Location, currentFile: line.Location.File),
			line.CommandLocation
		);

		return true;
	}
}
