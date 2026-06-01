using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;

namespace TRNGScriptCompiler.Tests.Support;

/// <summary>
/// Non-vacuous guards: fails when the shipped constants file or its NG definitions, which the rest
/// of the suite depends on, gain or lose content.
/// </summary>
[TestClass]
public sealed class ShippedConstantsGuardTests
{
	private const string ConstantsFileName = CompilerFileNames.ScripterConstants;

	[TestMethod]
	[TestCategory("Fast")]
	public void ConstantsFile_ResolvesFromTestOutput()
	{
		string constantsPath = Path.Combine(AppContext.BaseDirectory, ConstantsFileName);

		Assert.IsTrue(File.Exists(constantsPath), $"scripter_constants.txt must be copied to the test output directory: {constantsPath}");

		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));

		ConstantsFile.Load(constantsPath, diagnostics);

		Assert.IsEmpty(diagnostics.Recorded, "The shipped constants file must be plain Windows-1252 (no BOM).");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void NgCommandCatalog_ExposesTheSentinelCommands()
	{
		string constantsPath = Path.Combine(AppContext.BaseDirectory, ConstantsFileName);

		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));
		var catalog = NgCommandCatalog.Parse(ConstantsFile.Load(constantsPath, diagnostics).Lines, constantsPath, diagnostics);

		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);

		// Sentinel commands spanning every argument shape the matrix exercises.
		string[] sentinels = ["Settings=", "TriggerGroup=", "TriggerGroupWord=", "ImportFile=", "Plugin=", "Diagnostic=", "WindowTitle="];

		foreach (string sentinel in sentinels)
			Assert.IsTrue(catalog.Definitions.ContainsKey(sentinel), $"The constants file must define the sentinel command '{sentinel}'.");

		// The TriggerGroupWord rename rule must keep both names resolvable.
		Assert.AreEqual(NgCommandCode.TriggerGroupWord, catalog.Definitions["TriggerGroupWord="].Code);

		Assert.AreSequenceEqual(
			[NgArgumentType.Word, NgArgumentType.Word, NgArgumentType.Word, NgArgumentType.Word, NgArgumentType.Array],
			catalog.Definitions["TriggerGroupWord="].ArgumentTypes,
			"TriggerGroupWord= takes the argument types of its definition, Word:Word:Word:Word:Array."
		);
	}
}
