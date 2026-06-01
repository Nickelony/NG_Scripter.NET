namespace TRNGScriptCompiler.Kernel.Infrastructure;

internal static class ExceptionExtensions
{
	/// <summary>Tells whether the file system refused or failed an access to a file or folder.</summary>
	/// <returns>
	/// <see langword="true"/> for an <see cref="IOException"/> or an <see cref="UnauthorizedAccessException"/>,
	/// the faults every file read, listing and move of the library recovers from.
	/// </returns>
	internal static bool IsFileAccessFault(this Exception ex) => ex is IOException or UnauthorizedAccessException;

	/// <summary>Gives the message of an exception without its trailing period, so a diagnostic can append it.</summary>
	/// <remarks>
	/// A diagnostic message ends without punctuation, and the framework writes most file messages as full sentences.
	/// </remarks>
	internal static string Reason(this Exception ex) => ex.Message.TrimEnd().TrimEnd('.');
}
