namespace TRNGScriptCompiler.Constants;

/// <summary>The first term of an expression that its caller refused.</summary>
/// <param name="Term">The term in upper case.</param>
/// <param name="Kind">The kind of constant the term names, or <see langword="null"/> for a number.</param>
internal readonly record struct RejectedTerm(string Term, ConstantKind? Kind);
