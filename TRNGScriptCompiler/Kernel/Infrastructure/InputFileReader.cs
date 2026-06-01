namespace TRNGScriptCompiler.Kernel.Infrastructure;

/// <summary>Reads the bytes of an input file, and stops at a size cap even for a device of unknown length.</summary>
internal static class InputFileReader
{
	/// <summary>The most bytes the compiler reads from one input file: 256 MiB.</summary>
	internal const long MaxBytes = 256L * 1024 * 1024;

	/// <summary>Reads a whole file, or fails once it holds more than <see cref="MaxBytes"/>.</summary>
	/// <param name="path">The path of the file.</param>
	/// <returns>The bytes of the file.</returns>
	/// <exception cref="IOException">
	/// The file could not be read, or it holds more than <see cref="MaxBytes"/>.
	/// </exception>
	/// <remarks>
	/// The read goes through a stream, so a device such as <c>/dev/zero</c>, whose length is 0, reaches the cap
	/// instead of filling the memory. The message of the cap does not name the file, because a caller names it.
	/// </remarks>
	internal static byte[] ReadAllBytes(string path)
	{
		using var stream = new FileStream(
			path,
			mode: FileMode.Open,
			access: FileAccess.Read,
			share: FileShare.Read,
			bufferSize: 1,
			options: FileOptions.SequentialScan
		);

		return ReadAll(stream, MaxBytes);
	}

	/// <summary>Reads a stream to its end, or fails once it holds more than a cap.</summary>
	/// <param name="stream">The stream to read; its length is a hint only.</param>
	/// <param name="maxBytes">The most bytes the stream may hold.</param>
	/// <remarks>
	/// <see cref="ReadAllBytes"/> passes <see cref="MaxBytes"/>, and a test passes a small cap.
	/// </remarks>
	internal static byte[] ReadAll(Stream stream, long maxBytes)
	{
		long length = stream.CanSeek ? stream.Length : 0;

		if (length > maxBytes)
			throw TooLarge(maxBytes);

		byte[] buffer = new byte[length];
		int count = 0;

		while (true)
		{
			if (count == buffer.Length)
			{
				// The buffer is full: another byte means the file grew, or has no length to trust.
				int next = stream.ReadByte();

				if (next < 0)
					return buffer;

				if (count >= maxBytes)
					throw TooLarge(maxBytes);

				Array.Resize(ref buffer, (int)Math.Min(maxBytes, Math.Max(count * 2L, 4096)));
				buffer[count++] = (byte)next;
				continue;
			}

			int read = stream.Read(buffer, count, buffer.Length - count);

			if (read == 0)
				return buffer[..count];

			count += read;
		}
	}

	private static IOException TooLarge(long maxBytes)
	{
		const int mebibyte = 1024 * 1024;
		string limit = maxBytes % mebibyte == 0 ? $"{maxBytes / mebibyte} MiB" : $"{maxBytes} bytes";
		return new($"The file holds more than {limit}, the limit of one input file; split it or remove what the compile does not need");
	}
}
