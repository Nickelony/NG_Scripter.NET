namespace TRNGScriptCompiler.Script;

/// <summary>One argument of a classic command that the generic encoder reads, checks and writes.</summary>
/// <remarks>
/// A range check runs when <see cref="Holder"/> is set, before a byte field's -128 to 255 check, so the first
/// failing check names the error. The holder is what the value is, such as <c>a room number</c>, and a byte field
/// without a <see cref="Maximum"/> reports 255 as its greatest value.
/// </remarks>
internal sealed record ClassicField(ClassicFieldKind Kind, int Minimum = int.MinValue, int Maximum = int.MaxValue, string? Holder = null);
