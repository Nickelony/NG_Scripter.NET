using System.Text;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Kernel.Runtime;

[TestClass]
public sealed class TextInputReaderTests
{
	private const string Utf8Note = "'English.txt' has a UTF-8 byte-order mark; the file will be decoded as UTF-8";

	[TestMethod]
	[TestCategory("IO")]
	public void FileWithAByteOrderMark_WarnsAtTheFileEachTimeItIsRead()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		string path = WriteMarkedFile(workspace);
		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));

		string first = TextInputReader.ReadAllText(path, overlay: null, diagnostics);
		TextInputReader.ReadAllText(path, overlay: null, diagnostics);

		Assert.AreEqual("Café", first);
		Assert.HasCount(2, diagnostics.Recorded);
		Assert.AreEqual(Utf8Note, diagnostics.Recorded[0].Message);
		Assert.AreEqual(path, diagnostics.Recorded[0].SourceFile);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void FileInTheSetOfNotedFiles_WarnsOnce()
	{
		using var workspace = TestWorkspace.CreateEmpty();

		string path = WriteMarkedFile(workspace);

		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));
		var notedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		TextInputReader.ReadAllText(path, overlay: null, diagnostics, notedFiles);
		TextInputReader.ReadAllText(path, overlay: null, diagnostics, notedFiles);

		DiagnosticAssert.HasWarning(diagnostics: diagnostics.Recorded, line: null, message: Utf8Note, file: path);
		Assert.Contains(path, notedFiles);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void FileOfTheOverlay_GivesItsTextWithoutANote()
	{
		using var workspace = TestWorkspace.CreateEmpty();

		string path = WriteMarkedFile(workspace);

		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));
		var overlay = new VirtualFileSystem(new Dictionary<string, string> { [path] = "From the overlay" });

		string text = TextInputReader.ReadAllText(path, overlay, diagnostics);

		Assert.AreEqual("From the overlay", text);
		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void MissingFile_ThrowsForTheCallerToReactTo()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));

		string path = workspace.PathOf("Missing.txt");

		Assert.ThrowsExactly<FileNotFoundException>(() => TextInputReader.ReadAllText(path, overlay: null, diagnostics));
		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);
	}

	private static string WriteMarkedFile(TestWorkspace workspace)
	{
		string path = workspace.PathOf("English.txt");
		Encoding encoding = Encoding.UTF8;

		File.WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes("Café")]);
		return path;
	}
}
