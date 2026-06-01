using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;

namespace TRNGScriptCompiler.Pipeline;

/// <summary>
/// Encrypts in memory, with <see cref="LegacyCipher.EncryptImage"/>, the <c>pix</c> folder images
/// <c>IF_CRYPTIC</c> names.
/// </summary>
/// <remarks>
/// It reads the images but writes none; <see cref="OutputPublisher"/> stages the encrypted copies
/// with the other outputs.
/// </remarks>
internal sealed class ImageEncryptor
{
	private readonly CompilerLog _logger;
	private readonly DiagnosticSink _diagnostics;
	private readonly PathResolver _paths;

	private string? _pixEngineDirectory;
	private string? _pixDirectory;

	/// <remarks>
	/// The analysis passes the run's resolver, which keeps listings, because it writes no file.
	/// Publication passes <see cref="PathResolver.Uncached"/>, because the files it moves would make a listing stale.
	/// </remarks>
	internal ImageEncryptor(CompilerLog logger, DiagnosticSink diagnostics, PathResolver paths)
	{
		_logger = logger;
		_diagnostics = diagnostics;
		_paths = paths;
	}

	/// <summary>
	/// Resolves the paths of <c>imageN.bmp</c> and <c>@imageN.bmp</c> in the <c>pix</c> folder of an engine folder.
	/// </summary>
	/// <remarks>
	/// A caller resolves an image once and passes the result to each call that takes it, because every resolution lists
	/// the <c>pix</c> folder again when the resolver keeps no listings.
	/// </remarks>
	internal ImagePaths GetImagePaths(int imageId, string engineDirectoryPath)
	{
		// Resolving a full path lists every folder above it, so the pix folder resolves once and each image name resolves inside it.
		if (_pixDirectory is null || _pixEngineDirectory != engineDirectoryPath)
		{
			_pixDirectory = _paths.ResolveFullPath(Path.Combine(engineDirectoryPath, "pix"));
			_pixEngineDirectory = engineDirectoryPath;
		}

		return new ImagePaths(
			_pixDirectory,
			_paths.ResolveEntry(_pixDirectory, $"image{imageId}.bmp"),
			_paths.ResolveEntry(_pixDirectory, $"@image{imageId}.bmp")
		);
	}

	/// <summary>Returns whether <c>imageN.bmp</c> is missing but its encrypted copy exists.</summary>
	/// <remarks>
	/// Such an image needs no encryption, since an earlier compile did it, so the publication skips it.
	/// </remarks>
	internal static bool OnlyTheEncryptedImageExists(ImagePaths paths) => !File.Exists(paths.SourcePath) && File.Exists(paths.EncryptedPath);

	/// <summary>Checks, without changing it, that an image <c>IF_CRYPTIC</c> names can be encrypted.</summary>
	/// <remarks>
	/// It passes when <c>imageN.bmp</c> holds the bytes the cipher reverses or only its encrypted copy exists,
	/// and reports an error otherwise.
	/// </remarks>
	internal bool CheckImageToEncrypt(int imageId, string engineDirectoryPath, SourceLocation location)
	{
		try
		{
			ImagePaths paths = GetImagePaths(imageId, engineDirectoryPath);

			if (OnlyTheEncryptedImageExists(paths))
				return true;

			bool exists = File.Exists(paths.SourcePath);
			return CheckSourceImage(imageId, paths, exists, length: exists ? new FileInfo(paths.SourcePath).Length : 0, location);
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			_diagnostics.Error($"Cannot read image{imageId}.bmp: {ex.Reason()}", location, ex);
			return false;
		}
	}

	/// <summary>
	/// Reads an image <c>IF_CRYPTIC</c> names and encrypts it in memory, without changing a file,
	/// or returns <see langword="null"/> after an error.
	/// </summary>
	/// <remarks>
	/// It fails on a missing <c>imageN.bmp</c>. A caller skips an image
	/// that <see cref="OnlyTheEncryptedImageExists"/> reports.
	/// </remarks>
	internal EncryptedImage? EncryptImage(int imageId, ImagePaths paths, SourceLocation location)
	{
		try
		{
			bool exists = File.Exists(paths.SourcePath);
			byte[] bytes = [];

			if (exists)
			{
				_logger.LogVerbose($"Encrypting image{imageId}.bmp", indent: 2);
				bytes = InputFileReader.ReadAllBytes(paths.SourcePath);
			}

			if (!CheckSourceImage(imageId, paths, exists, length: bytes.Length, location))
				return null;

			LegacyCipher.EncryptImage(bytes);
			return new EncryptedImage(paths.SourcePath, paths.EncryptedPath, bytes);
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			_diagnostics.Error($"Failed encrypting image{imageId}.bmp: {ex.Reason()}", location, ex);
			return null;
		}
	}

	/// <summary>
	/// Reports an error when <c>imageN.bmp</c> is missing or holds fewer bytes than the cipher reverses.
	/// </summary>
	/// <returns><see langword="true"/> when the image exists and holds the bytes the cipher reverses.</returns>
	/// <remarks>
	/// The caller passes what it learned of the file, because the check reads the file in one place and the encryption
	/// reads it in another. <paramref name="length"/> is read only when <paramref name="exists"/> holds.
	/// </remarks>
	private bool CheckSourceImage(int imageId, ImagePaths paths, bool exists, long length, SourceLocation location)
	{
		if (!exists)
		{
			_diagnostics.Error($"Cannot find image{imageId}.bmp to encrypt in {paths.PixDirectory} directory", location);
			return false;
		}

		if (length < LegacyCipher.ImageReversedLength)
		{
			_diagnostics.Error(
				$"image{imageId}.bmp is too small to encrypt: expected at least {LegacyCipher.ImageReversedLength} bytes, found {length} bytes",
				location
			);

			return false;
		}

		return true;
	}

	/// <summary>
	/// Warns when an image the script names without <c>IF_CRYPTIC</c> exists only as its encrypted copy.
	/// </summary>
	/// <remarks>
	/// Encryption deletes <c>imageN.bmp</c> (Module1.bas:8161), so a script that drops the flag later leaves the engine
	/// without the bitmap.
	/// </remarks>
	internal void WarnIfOnlyTheEncryptedImageExists(int imageId, string engineDirectoryPath, SourceLocation location)
	{
		if (OnlyTheEncryptedImageExists(GetImagePaths(imageId, engineDirectoryPath)))
		{
			_diagnostics.Warning(
				$"The pix folder holds only @image{imageId}.bmp, the encrypted copy of image{imageId}.bmp, and without IF_CRYPTIC the engine looks for image{imageId}.bmp; add IF_CRYPTIC, or restore image{imageId}.bmp",
				location
			);
		}
	}
}
