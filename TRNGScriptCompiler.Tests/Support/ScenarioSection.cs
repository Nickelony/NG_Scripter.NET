namespace TRNGScriptCompiler.Tests.Support;

/// <summary>The script section a coverage command is inserted into.</summary>
public enum ScenarioSection
{
	/// <summary>The <c>[Language]</c> section.</summary>
	Language,

	/// <summary>The <c>[Options]</c> section.</summary>
	Options,

	/// <summary>The <c>[PSXExtensions]</c> section.</summary>
	PsxExtensions,

	/// <summary>The <c>[PCExtensions]</c> section.</summary>
	PcExtensions,

	/// <summary>The <c>[Title]</c> section.</summary>
	Title,

	/// <summary>The <c>[Level]</c> section.</summary>
	Level,
}
