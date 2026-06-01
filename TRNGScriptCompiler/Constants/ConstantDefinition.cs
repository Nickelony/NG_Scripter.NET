namespace TRNGScriptCompiler.Constants;

/// <summary>
/// A constant that a constants file or a plugin script defines, with the 1-based line of its definition.
/// </summary>
internal readonly record struct ConstantDefinition(int Value, int Line);
