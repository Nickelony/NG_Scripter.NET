using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>Collects the classic lines and the NG lines of one <c>[Level]</c> or <c>[Title]</c> section.</summary>
internal sealed class LevelSectionBuilder
{
	private readonly DiagnosticSink _diagnostics;
	private readonly ScriptSectionType _sectionType;
	private readonly IReadOnlySet<string> _ngCommandNames;
	private readonly SourceLocation _headerLocation;
	private readonly List<ScriptCommandLine> _lines = [];
	private readonly List<NgLine> _ngLines = [];

	private string _levelName = string.Empty;
	private string _levelPath = string.Empty;

	private SourceLocation _levelPathLocation;

	/// <remarks>
	/// The NG command names carry their <c>=</c>, and the header location names the line that opens the section.
	/// </remarks>
	internal LevelSectionBuilder(
		DiagnosticSink diagnostics,
		ScriptSectionType sectionType,
		IReadOnlySet<string> ngCommandNames,
		SourceLocation headerLocation
	)
	{
		_diagnostics = diagnostics;
		_sectionType = sectionType;
		_ngCommandNames = ngCommandNames;
		_headerLocation = headerLocation;
	}

	/// <summary>
	/// Adds a command line of the section, returning <see langword="false"/> after an error when it names
	/// no known command.
	/// </summary>
	/// <remarks>
	/// A classic command takes precedence over an NG command of the same name, as VB6 checks both tables as it reads
	/// (<c>Module1.bas:5302-5332</c>).
	/// </remarks>
	internal bool TryAdd(ScriptCommandLine line)
	{
		if (ClassicCommandCatalog.Contains(line.Command))
		{
			_lines.Add(line);

			// VB6 reads the name as the rest of the line, commas included (Module1.bas:5995-6001).
			if (line.Command.IgnoreCaseEquals(ClassicCommandCatalog.NameCommand))
				_levelName = ScriptLineLexer.GetRawRemainder(line.Text);

			if (line.Command.IgnoreCaseEquals(ClassicCommandCatalog.LevelCommand) && line.Arguments.Count >= 1)
			{
				_levelPath = line.Arguments[0];
				_levelPathLocation = line.ArgumentLocation(0);
			}

			return true;
		}

		if (_ngCommandNames.Contains(line.Command))
		{
			_ngLines.Add(new NgLine(line));
			return true;
		}

		_diagnostics.Error(ScriptCommandMessages.UnknownCommand(ScriptSectionHeaders.Of(_sectionType), line.Command), line.CommandLocation);
		return false;
	}

	/// <summary>
	/// Adds a <c>#DEFINE @plugin</c> line, which applies where it stands among the NG lines of the section.
	/// </summary>
	/// <remarks>
	/// The line is normalized, and the source text is the first physical line as the file holds it, up to its comment.
	/// </remarks>
	internal void AddPluginDefine(string line, string sourceText, SourceLocation location) =>
		_ngLines.Add(NgLine.PluginDefine(line, sourceText, location));

	/// <summary>Builds the section from the lines added so far.</summary>
	internal ParsedSection Build()
	{
		return new()
		{
			SectionType = _sectionType,
			LevelName = _levelName,
			LevelPath = _levelPath,
			LevelPathLocation = _levelPathLocation,
			HeaderLocation = _headerLocation,
			ClassicLines = [.. _lines],
			NgLines = [.. _ngLines],
		};
	}
}
