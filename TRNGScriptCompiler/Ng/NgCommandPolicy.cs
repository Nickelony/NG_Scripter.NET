using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Ng;

/// <summary>The parser rules of an NG command that its definition in the constants file does not carry.</summary>
/// <remarks>
/// The definition keeps the <c>*</c>
/// that marks an <c>[Options]</c> command. <see cref="WordForm"/> applies when no argument carries a plugin ID;
/// <see cref="Groups"/> gives the first grouped value, group size and name.
/// </remarks>
internal sealed record NgCommandPolicy(
	NgDuplicateRule Duplicates,
	bool IsTitleOnly = false,
	NgCommandCode? WordForm = null,
	NgArgumentGroups? Groups = null
);
