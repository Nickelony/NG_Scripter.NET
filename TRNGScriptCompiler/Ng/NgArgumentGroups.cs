namespace TRNGScriptCompiler.Ng;

/// <summary>How the help groups the values of a command's trailing array.</summary>
/// <param name="FirstValue">The number of arguments that come before the first group.</param>
/// <param name="Size">The number of values in one group.</param>
/// <param name="Name">What one group is called, which a warning about a partial group names.</param>
internal readonly record struct NgArgumentGroups(int FirstValue, int Size, string Name);
