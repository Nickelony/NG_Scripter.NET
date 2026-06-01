using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TRNGScriptCompiler.Kernel.Text;

/// <summary>
/// Extracts the trailing number immediately before a file name's last dot,
/// mirroring VB6's <c>TrovaNumeroFile</c> helper.
/// </summary>
/// <remarks>
/// A name without a dot yields <c>-1</c>, a dot without digits before it yields <c>0</c>, and a value
/// above 32767 fails, as VB6's Integer overflows.
/// </remarks>
internal static class FileNameNumber
{
	/// <summary>Attempts to extract the file number from a file name as the script command writes it.</summary>
	internal static bool TryParse(string fileName, out int fileNumber, [NotNullWhen(false)] out string? error)
	{
		fileNumber = 0;
		error = null;

		int dotIndex = fileName.LastIndexOf('.');

		// VB6 gives a name without an extension no number (-1), not zero.
		if (dotIndex < 0)
		{
			fileNumber = -1;
			return true;
		}

		int digitEnd = dotIndex - 1;
		int digitStart = digitEnd;

		while (digitStart >= 0 && char.IsAsciiDigit(fileName[digitStart]))
			digitStart--;

		if (digitStart == digitEnd)
			return true;

		string digits = fileName[(digitStart + 1)..dotIndex];

		// A digit run past int overflows VB6's Integer as well, so it reads as the same 16-bit error.
		bool fitsInt = int.TryParse(s: digits, style: NumberStyles.None, provider: CultureInfo.InvariantCulture, result: out fileNumber);

		if (!fitsInt || fileNumber > short.MaxValue)
		{
			string shown = fitsInt ? fileNumber.ToString(CultureInfo.InvariantCulture) : digits;
			error = $"The file number {shown} in '{fileName}' exceeds the 16-bit range (VB6 raised an overflow error here)";
			return false;
		}

		return true;
	}
}
