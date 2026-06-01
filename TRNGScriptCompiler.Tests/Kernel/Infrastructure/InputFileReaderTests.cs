using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Kernel.Infrastructure;

[TestClass]
public sealed class InputFileReaderTests
{
	/// <summary>A stream with no length that yields a number of zero bytes, as /dev/zero does without end.</summary>
	private sealed class ZeroStream(long available) : Stream
	{
		private long _remaining = available;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override void Flush() { }

		public override int Read(byte[] buffer, int offset, int count)
		{
			int read = (int)Math.Min(count, _remaining);

			Array.Clear(buffer, offset, read);
			_remaining -= read;

			return read;
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ReadAllBytes_ReturnsTheBytesOfTheFile()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		string path = workspace.PathOf("input.bin");

		File.WriteAllBytes(path, [1, 2, 3]);

		Assert.AreSequenceEqual(new byte[] { 1, 2, 3 }, InputFileReader.ReadAllBytes(path));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ReadAll_StreamWithoutALengthThatNeverEnds_StopsAtTheCap()
	{
		var endless = new ZeroStream(long.MaxValue);
		IOException error = Assert.ThrowsExactly<IOException>(() => InputFileReader.ReadAll(endless, maxBytes: 10_000));

		Assert.AreEqual(
			"The file holds more than 10000 bytes, the limit of one input file; split it or remove what the compile does not need",
			error.Message
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ReadAll_StreamWithoutALengthOfExactlyTheCap_IsRead() =>
		Assert.HasCount(5_000, InputFileReader.ReadAll(new ZeroStream(5_000), maxBytes: 5_000));

	[TestMethod]
	[TestCategory("Fast")]
	public void ReadAll_StreamWithoutALengthOneByteOverTheCap_Fails() =>
		Assert.ThrowsExactly<IOException>(() => InputFileReader.ReadAll(new ZeroStream(5_001), maxBytes: 5_000));

	[TestMethod]
	[TestCategory("IO")]
	public void ScriptFileJustOverTheCap_FailsTheCompileNamingTheFileAndTheLimit()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		string script = workspace.PathOf(Path.Combine("script", "Script.txt"));

		// A sparse file of one byte over the cap, so the test writes nothing.
		using (var stream = new FileStream(script, FileMode.Create, FileAccess.Write))
			stream.SetLength(InputFileReader.MaxBytes + 1);

		TestCompileResult result = workspace.Compile();

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: null,
			message: $"Cannot read file {script}: The file holds more than 256 MiB, the limit of one input file; split it or remove what the compile does not need",
			file: script
		);
	}
}
