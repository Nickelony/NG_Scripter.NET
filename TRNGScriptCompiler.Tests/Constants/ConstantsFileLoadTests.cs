using System.Text;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Constants;

[TestClass]
public sealed class ConstantsFileLoadTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void CustomConstantsPath_IsHonoredForCommandDefinitions()
	{
		using var workspace = TestWorkspace.CreateClean("CustomSnow= 5\n");

		string customConstantsPath = workspace.PathOf("custom_constants.txt");

		// 'CustomSnow=' exists only in this file; the default scripter_constants.txt is not consulted.
		File.WriteAllText(customConstantsPath, "<START_NEW_COMMANDS>\n2#1#CustomSnow=:Word\n<END>\n");

		TRNGCompilerOptions options = workspace.CreateOptions() with { ConstantsFilePath = customConstantsPath };
		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		CompileResult result = compiler.Compile(options);

		Assert.IsTrue(result.Success, $"Custom definitions must be resolved from ConstantsFilePath: {result.Log}");

		// CustomSnow= is a known command only when the custom constants file is honored, so the compile reports nothing.
		DiagnosticAssert.NoDiagnostics(result.Diagnostics);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void MissingConstantsFile_Fails()
	{
		using var workspace = TestWorkspace.Create();

		TRNGCompilerOptions options = workspace.CreateOptions() with { ConstantsFilePath = workspace.PathOf("does-not-exist.txt") };
		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		CompileResult result = compiler.Compile(options);

		Assert.IsFalse(result.Success, "A missing constants file must fail the compile.");
		DiagnosticAssert.SingleError(result.Diagnostics, line: null, $"Constants file was not found: {options.ConstantsFilePath}");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ConstantsFile_WithoutDefinitions_Fails()
	{
		using var workspace = TestWorkspace.Create();
		string emptyConstantsPath = workspace.PathOf("empty_constants.txt");

		File.WriteAllText(emptyConstantsPath, "; no command definitions here\n");

		TRNGCompilerOptions options = workspace.CreateOptions() with { ConstantsFilePath = emptyConstantsPath };
		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		CompileResult result = compiler.Compile(options);

		Assert.IsFalse(result.Success, "A constants file without NG definitions must fail fast.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: null,
			message: $"No NG command definitions were found in: {emptyConstantsPath}",
			file: emptyConstantsPath
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ConstantsFile_WithUnicodeBom_IsDecodedAndWarns()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		string unicodeConstantsPath = workspace.PathOf("unicode_constants.txt");

		string realConstants = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, CompilerFileNames.ScripterConstants));
		File.WriteAllText(unicodeConstantsPath, realConstants, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

		TRNGCompilerOptions options = workspace.CreateOptions() with { ConstantsFilePath = unicodeConstantsPath };
		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		CompileResult result = compiler.Compile(options);

		Assert.IsTrue(result.Success, $"A UTF-16 constants file must still compile: {result.Log}");

		DiagnosticAssert.HasWarning(
			diagnostics: result.Diagnostics,
			line: null,
			message: "'unicode_constants.txt' has a UTF-16 (little-endian) byte-order mark; the file will be decoded as UTF-16",
			file: unicodeConstantsPath
		);
	}
}
