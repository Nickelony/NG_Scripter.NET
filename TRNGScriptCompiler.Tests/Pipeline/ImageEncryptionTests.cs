using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Pipeline;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

[TestClass]
public sealed class ImageEncryptionTests
{
	private static byte[] CreateImageBytes(int length)
	{
		byte[] bytes = new byte[length];

		for (int i = 0; i < length; i++)
			bytes[i] = (byte)(i * 7);

		return bytes;
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImageEncryption_ChangesNoFile()
	{
		// VB6 deletes imageN.bmp before it writes @imageN.bmp (Module1.bas:8161, 8183); the port encrypts in memory, and the publication
		// stages the copy with the other outputs.
		using var workspace = TestWorkspace.CreateEmpty();
		byte[] sourceBytes = CreateImageBytes(256);
		workspace.WithBinaryFile(Path.Combine("pix", "image5.bmp"), sourceBytes);

		var logger = new CompilerLog(conciseLogging: false);
		var encryptor = new ImageEncryptor(logger, new DiagnosticSink(logger), PathResolver.Uncached);

		string sourceImagePath = workspace.PathOf(Path.Combine("pix", "image5.bmp"));
		string encryptedImagePath = workspace.PathOf(Path.Combine("pix", "@image5.bmp"));

		EncryptedImage? image = encryptor.EncryptImage(
			imageId: 5,
			paths: encryptor.GetImagePaths(imageId: 5, engineDirectoryPath: workspace.RootPath),
			location: new SourceLocation(10)
		);

		Assert.IsNotNull(image);

		byte[] expected = [.. sourceBytes];
		LegacyCipher.EncryptImage(expected);

		Assert.AreSequenceEqual(expected, image.Bytes);
		Assert.AreEqual(encryptedImagePath, image.EncryptedPath, ignoreCase: true);
		Assert.AreSequenceEqual(sourceBytes, File.ReadAllBytes(sourceImagePath), "image5.bmp is left as it was.");
		Assert.IsFalse(File.Exists(encryptedImagePath), "@image5.bmp is not written.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OnlyTheEncryptedImageExists_HoldsOnlyForAMissingImageWithItsEncryptedCopy()
	{
		using var workspace = TestWorkspace.CreateEmpty();

		workspace.WithBinaryFile(Path.Combine("pix", "image1.bmp"), CreateImageBytes(256));
		workspace.WithBinaryFile(Path.Combine("pix", "@image2.bmp"), CreateImageBytes(256));
		workspace.WithBinaryFile(Path.Combine("pix", "image3.bmp"), CreateImageBytes(256));
		workspace.WithBinaryFile(Path.Combine("pix", "@image3.bmp"), CreateImageBytes(256));

		var logger = new CompilerLog(conciseLogging: false);
		var encryptor = new ImageEncryptor(logger, new DiagnosticSink(logger), PathResolver.Uncached);

		Assert.IsFalse(ImageEncryptor.OnlyTheEncryptedImageExists(encryptor.GetImagePaths(1, workspace.RootPath)), "Only the image exists.");
		Assert.IsTrue(ImageEncryptor.OnlyTheEncryptedImageExists(encryptor.GetImagePaths(2, workspace.RootPath)), "Only the encrypted copy exists.");
		Assert.IsFalse(ImageEncryptor.OnlyTheEncryptedImageExists(encryptor.GetImagePaths(3, workspace.RootPath)), "Both exist.");
		Assert.IsFalse(ImageEncryptor.OnlyTheEncryptedImageExists(encryptor.GetImagePaths(4, workspace.RootPath)), "Neither exists.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void EncryptImage_WithOnlyTheEncryptedCopy_FailsOnTheMissingImage()
	{
		using var workspace = TestWorkspace.CreateEmpty();

		workspace.WithBinaryFile(Path.Combine("pix", "@image2.bmp"), CreateImageBytes(256));

		var logger = new CompilerLog(conciseLogging: false);
		var diagnostics = new DiagnosticSink(logger);
		var encryptor = new ImageEncryptor(logger, diagnostics, PathResolver.Uncached);

		Assert.IsNull(
			encryptor.EncryptImage(
				imageId: 2,
				paths: encryptor.GetImagePaths(imageId: 2, engineDirectoryPath: workspace.RootPath),
				location: new SourceLocation(9)
			)
		);

		DiagnosticAssert.SingleError(diagnostics.Recorded, 9, $"Cannot find image2.bmp to encrypt in {workspace.PathOf("pix")} directory");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OneEncryptor_ReadsThePixFolderOfEachEngineFolderItIsGiven()
	{
		// The encryptor keeps the pix folder it resolved, so a call for another engine folder must resolve that folder's own.
		using var withImage = TestWorkspace.CreateEmpty();
		using var withoutImage = TestWorkspace.CreateEmpty();

		withImage.WithBinaryFile(Path.Combine("pix", "image5.bmp"), CreateImageBytes(256));
		Directory.CreateDirectory(withoutImage.PathOf("pix"));

		var logger = new CompilerLog(conciseLogging: false);
		var diagnostics = new DiagnosticSink(logger);
		var encryptor = new ImageEncryptor(logger, diagnostics, new PathResolver());

		Assert.IsTrue(encryptor.CheckImageToEncrypt(imageId: 5, engineDirectoryPath: withImage.RootPath, location: new SourceLocation(3)));
		Assert.IsFalse(encryptor.CheckImageToEncrypt(imageId: 5, engineDirectoryPath: withoutImage.RootPath, location: new SourceLocation(7)));

		DiagnosticAssert.SingleError(diagnostics.Recorded, 7, $"Cannot find image5.bmp to encrypt in {withoutImage.PathOf("pix")} directory");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void CrypticImage_CommandRenamesAndTransformsImage()
	{
		const string script = TestScripts.ImageLevel + TestScripts.CrypticImage;

		byte[] sourceBytes = [.. Enumerable.Range(0, 300).Select(index => (byte)(index % 256))];

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Image Level"));
		workspace.WithBinaryFile(Path.Combine("pix", "image4.bmp"), sourceBytes);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		string sourcePath = workspace.PathOf(Path.Combine("pix", "image4.bmp"));
		string encryptedPath = workspace.PathOf(Path.Combine("pix", "@image4.bmp"));

		Assert.IsFalse(File.Exists(sourcePath), "Original image should be removed after encrypting.");
		Assert.IsTrue(File.Exists(encryptedPath), "Encrypted image should be written with the @image prefix.");

		byte[] expected = [.. sourceBytes];
		LegacyCipher.EncryptImage(expected);

		Assert.AreNotSequenceEqual(sourceBytes, expected, "The cipher alters the image bytes.");
		Assert.AreSequenceEqual(expected, File.ReadAllBytes(encryptedPath));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void CrypticImage_OverALongerEncryptedFile_WritesOnlyTheNewBytes()
	{
		// VB6 opens @imageN.bmp for Binary without truncating it (Module1.bas:8182), so a longer file left from an earlier
		// compile keeps its tail after the 300 new bytes. The port writes a new file instead, so the tail is gone (PD-45).
		const string script = """
			[Language]
			File= 0,English.txt

			[Level]
			Name= Image Level
			Level= data\image,0
			LoadCamera= 0,0,0,0,0,0,0
			Image= 1,4,IF_CRYPTIC,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE
			""";

		byte[] sourceBytes = [.. Enumerable.Range(0, 300).Select(index => (byte)(index % 256))];
		string encryptedPath = Path.Combine("pix", "@image4.bmp");

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Image Level"));

		workspace.WithBinaryFile(Path.Combine("pix", "image4.bmp"), sourceBytes);
		workspace.WithBinaryFile(encryptedPath, [.. Enumerable.Repeat((byte)0xEE, 400)]);

		using var fresh = TestWorkspace.Create(script);
		fresh.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Image Level"));
		fresh.WithBinaryFile(Path.Combine("pix", "image4.bmp"), sourceBytes);

		TestCompileResult result = workspace.Compile();
		TestCompileResult freshResult = fresh.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.IsTrue(freshResult.Success, freshResult.Log);
		Assert.AreEqual(300, new FileInfo(workspace.PathOf(encryptedPath)).Length, "The old file's last 100 bytes are gone.");

		Assert.AreSequenceEqual(
			fresh.ReadOutput(encryptedPath),
			workspace.ReadOutput(encryptedPath),
			"The 300 bytes are those a fresh compile writes."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void CrypticImage_ShortBitmap_FailsWithClearDiagnostic()
	{
		const string script = TestScripts.ImageLevel + TestScripts.CrypticImage;

		byte[] sourceBytes = [.. Enumerable.Range(0, 58).Select(index => (byte)index)];

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Image Level"));
		workspace.WithBinaryFile(Path.Combine("pix", "image4.bmp"), sourceBytes);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "Short cryptic images should fail with a clear diagnostic.");
		DiagnosticAssert.SingleError(result.Diagnostics, 10, "image4.bmp is too small to encrypt: expected at least 256 bytes, found 58 bytes");

		string sourcePath = workspace.PathOf(Path.Combine("pix", "image4.bmp"));
		string encryptedPath = workspace.PathOf(Path.Combine("pix", "@image4.bmp"));

		Assert.IsTrue(File.Exists(sourcePath), "The source image should not be deleted when encrypting fails.");
		Assert.IsFalse(File.Exists(encryptedPath), "No encrypted output should be produced for a short bitmap.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void CrypticImage_IgnoredFlags_SkipEncryptionEntirely()
	{
		// PD-13: flags -1 (arguments absent) mean IGNORE, exactly like VB6 (Module1.bas:8505-8511).
		const string script = TestScripts.ImageLevel + "Image= 1,4,-1,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE\n";

		// pix\image4.bmp intentionally does not exist: if the -1 flags were not treated as
		// IGNORE, the encoder would try to encrypt the missing file and fail.
		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Image Level"));

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
	}
}
