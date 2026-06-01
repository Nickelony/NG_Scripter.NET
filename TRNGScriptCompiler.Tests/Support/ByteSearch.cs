namespace TRNGScriptCompiler.Tests.Support;

/// <summary>Finds a byte sequence anywhere in compiled output.</summary>
/// <remarks>
/// Prefer <see cref="ScriptDatReader"/> for <c>Script.dat</c>: a search over the whole file
/// also reads the random security padding, so search the bytes of one field or section at most. A language <c>.dat</c>
/// has no reader, and holds no padding.
/// </remarks>
internal static class ByteSearch
{
	public static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) => haystack.IndexOf(needle) >= 0;
}
