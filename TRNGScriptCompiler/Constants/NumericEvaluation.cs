namespace TRNGScriptCompiler.Constants;

/// <summary>
/// Holds the outcome of evaluating a numeric expression, with the plugin ID of the last plugin constant it names, or 0.
/// </summary>
/// <remarks>
/// A failure has a 0 value and an <see cref="Error"/> naming the fix or a <see cref="Rejection"/>,
/// the first refused term, with no kind for a number.
/// </remarks>
internal readonly record struct NumericEvaluation(bool Success, int Value, int PluginId, string? Error, RejectedTerm? Rejection);
