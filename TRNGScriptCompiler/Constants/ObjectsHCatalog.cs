using System.Text.RegularExpressions;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;

namespace TRNGScriptCompiler.Constants;

/// <summary>Holds the slot and static constants that it reads from <c>Objects.h</c>.</summary>
internal sealed partial class ObjectsHCatalog
{
	private readonly CompilerLog _logger;
	private readonly DiagnosticSink _diagnostics;

	private readonly Dictionary<string, int> _slotConstants = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, int> _staticConstants = new(StringComparer.OrdinalIgnoreCase);

	internal ObjectsHCatalog(CompilerLog logger, DiagnosticSink diagnostics)
	{
		_logger = logger;
		_diagnostics = diagnostics;
	}

	/// <summary>
	/// Gets the number of moveable slots, the position of <c>NUMBER_OBJECTS</c> in <c>object_types</c>.
	/// </summary>
	/// <remarks>
	/// <see langword="null"/> until an <c>Objects.h</c> that lists <c>NUMBER_OBJECTS</c> is loaded.
	/// </remarks>
	internal int? SlotCount => _slotConstants.TryGetValue("NUMBER_OBJECTS", out int count) ? count : null;

	/// <summary>Loads slot and static constants from <c>Objects.h</c>.</summary>
	/// <remarks>
	/// A missing or unreadable file, or an enum the file lacks, only warns, and leaves those names unknown.
	/// </remarks>
	internal void Load(string objectsHPath, VirtualFileSystem? overlay = null)
	{
		try
		{
			// The log line of a diagnostic without a line names no file, so the message keeps the path.
			if (!VirtualFileSystem.FileExists(objectsHPath, overlay))
			{
				_diagnostics.Warning($"Objects.h was not found: {objectsHPath}", SourceLocation.InFile(objectsHPath));
				return;
			}

			_logger.LogVerbose($"Loading slot constants from: {objectsHPath}");

			string content = TextInputReader.ReadAllText(objectsHPath, overlay, _diagnostics);

			// Parse enum object_types
			ParseEnum(objectsHPath, content, enumName: "object_types", enumPattern: ObjectTypesEnumPattern(), targetDict: _slotConstants);

			// Parse enum static_types
			ParseEnum(objectsHPath, content, enumName: "static_types", enumPattern: StaticTypesEnumPattern(), targetDict: _staticConstants);

			_logger.LogVerbose($"Loaded {_slotConstants.Count} slot constants and {_staticConstants.Count} static constants");
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			// An unreadable Objects.h leaves the slot and static names unknown, as a missing one does.
			_diagnostics.Warning(
				$"Objects.h cannot be read, so its slot and static names are unknown: {ex.Reason()}",
				SourceLocation.InFile(objectsHPath),
				ex
			);
		}
	}

	[GeneratedRegex(@"enum\s+object_types\s*\{([^}]+)\}", RegexOptions.Singleline)]
	private static partial Regex ObjectTypesEnumPattern();

	[GeneratedRegex(@"enum\s+static_types\s*\{([^}]+)\}", RegexOptions.Singleline)]
	private static partial Regex StaticTypesEnumPattern();

	private void ParseEnum(string objectsHPath, string content, string enumName, Regex enumPattern, Dictionary<string, int> targetDict)
	{
		// Find enum declaration
		Match match = enumPattern.Match(content);

		if (!match.Success)
		{
			_diagnostics.Warning($"Could not find enum {enumName} in Objects.h", SourceLocation.InFile(objectsHPath));
			return;
		}

		// VB6 ends an item at every comma, drops spaces, tabs and line breaks, and skips an empty item (Module1.bas:9052-9068). The
		// port strips a // comment first, where VB6 glues it to the next item. The strip keeps every line break, so the breaks before an
		// item count its line from the line that opens the body.
		string enumBody = string.Join('\n', match.Groups[1].Value.Split('\n').Select(line => line.StripInlineComment("//")));

		int bodyLine = 1 + content.AsSpan(0, match.Groups[1].Index).Count('\n');
		int position = 0;
		int itemOffset = 0;

		foreach (string rawItem in enumBody.Split(','))
		{
			int rawItemOffset = itemOffset;
			itemOffset += rawItem.Length + 1;

			string item = string.Concat(rawItem.Where(character => character is not (' ' or '\t' or '\r' or '\n')));

			if (item.Length == 0)
				continue;

			int itemPosition = position++;
			int leadingSpace = rawItem.Length - rawItem.TrimStart(' ', '\t', '\r', '\n').Length;

			var location = new SourceLocation(bodyLine + enumBody.AsSpan(0, rawItemOffset + leadingSpace).Count('\n'), File: objectsHPath);

			// VB6 keeps NAME=value as the item text, so NAME never resolves and the item keeps its position.
			int equalsIndex = item.IndexOf('=');

			if (equalsIndex >= 0)
			{
				string assignedName = item[..equalsIndex];

				_diagnostics.Warning(
					$"Objects.h item {item} in enum {enumName} assigns a value, so {assignedName} does not resolve, as in VB6; remove the assignment to use the name",
					location
				);

				continue;
			}

			// VB6 keeps both items and finds the first (Module1.bas:10139-10140, 10158-10162), so later items keep their positions.
			if (!targetDict.TryAdd(item, itemPosition))
			{
				_diagnostics.Warning(
					$"Objects.h lists {item} twice in enum {enumName}; it resolves to its first position, {targetDict[item]}",
					location
				);
			}
		}
	}

	/// <summary>Finds a slot constant, matching its name without regard to case.</summary>
	internal bool TryGetSlotConstant(string name, out int value) => _slotConstants.TryGetValue(name, out value);

	/// <summary>Finds a static constant, matching its name without regard to case.</summary>
	internal bool TryGetStaticConstant(string name, out int value) => _staticConstants.TryGetValue(name, out value);
}
