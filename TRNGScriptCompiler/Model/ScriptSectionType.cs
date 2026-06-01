namespace TRNGScriptCompiler.Model;

/// <summary>Identifies the kind of a section of <c>Script.txt</c>.</summary>
internal enum ScriptSectionType
{
	/// <summary>No section yet: lines before the first recognized section header.</summary>
	None,

	/// <summary>The <c>[PSXExtensions]</c> section.</summary>
	PsxExtensions,

	/// <summary>The <c>[PCExtensions]</c> section.</summary>
	PcExtensions,

	/// <summary>The <c>[Language]</c> section.</summary>
	Language,

	/// <summary>The <c>[Options]</c> section.</summary>
	Options,

	/// <summary>The <c>[Title]</c> section.</summary>
	Title,

	/// <summary>A <c>[Level]</c> section.</summary>
	Level,
}
