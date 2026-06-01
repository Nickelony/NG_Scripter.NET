using System.Text;

namespace TRNGScriptCompiler.Tests.Support;

/// <summary>
/// Compares compiled bytes with a golden output and, on a mismatch, says where the first difference is.
/// </summary>
internal static class GoldenAssert
{
	private const int WindowBytes = 16;

	/// <summary>Asserts that a compiled file matches its golden bytes.</summary>
	/// <param name="expected">The golden bytes.</param>
	/// <param name="actualPath">The compiled file; a mismatch attaches it to the test result.</param>
	/// <param name="context">The running test's context, which receives the attachment.</param>
	public static void FileMatches(byte[] expected, string actualPath, TestContext context)
	{
		byte[] actual = File.ReadAllBytes(actualPath);
		int offset = FirstDifference(expected, actual);

		if (offset < 0)
			return;

		context.AddResultFile(actualPath);

		string fileName = Path.GetFileName(actualPath);
		var message = new StringBuilder();
		message.AppendLine($"{fileName} differs from the golden output at offset 0x{offset:X} ({offset}): {Describe(fileName, expected, offset)}.");

		if (expected.Length != actual.Length)
			message.AppendLine($"Length: expected {expected.Length} bytes, got {actual.Length}.");

		message.AppendLine($"Expected: {HexWindow(expected, offset)}");
		message.Append($"Actual:   {HexWindow(actual, offset)}");
		Assert.Fail(message.ToString());
	}

	private static int FirstDifference(byte[] expected, byte[] actual)
	{
		int common = expected.AsSpan().CommonPrefixLength(actual);
		return common == expected.Length && common == actual.Length ? -1 : common;
	}

	private static string Describe(string fileName, byte[] expected, int offset)
	{
		if (!fileName.Equals("Script.dat", StringComparison.OrdinalIgnoreCase) || offset >= expected.Length)
			return offset >= expected.Length ? "past the end of the golden output" : "language file";

		try
		{
			return ScriptDatReader.Read(expected).Describe(offset);
		}
		catch (InvalidDataException)
		{
			return "the golden output does not parse as a Script.dat";
		}
	}

	private static string HexWindow(byte[] bytes, int offset)
	{
		int start = Math.Max(0, offset - WindowBytes);
		int end = Math.Min(bytes.Length, offset + WindowBytes);

		var window = new StringBuilder();

		for (int i = start; i < end; i++)
			window.Append(i == offset ? $"[{bytes[i]:X2}]" : $" {bytes[i]:X2} ");

		return $"0x{start:X}: {window}";
	}
}
