using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

[TestClass]
public sealed class CancellationTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void Analyze_Throws_WhenTokenAlreadyCanceled()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		using var cts = new CancellationTokenSource();
		cts.Cancel();

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();

		Assert.ThrowsExactly<OperationCanceledException>(() => compiler.Analyze(workspace.CreateOptions(), cancellationToken: cts.Token));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Analyze_Throws_WhenTokenIsCanceledWhileTheOutputsCompose()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		using var cts = new CancellationTokenSource();

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();

		compiler.LogEntryProduced += (_, e) =>
		{
			if (e.Entry.Message == "SAVE LANGUAGES.DAT ...")
				cts.Cancel();
		};

		Assert.ThrowsExactly<OperationCanceledException>(() => compiler.Analyze(workspace.CreateOptions(), cancellationToken: cts.Token));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Analyze_CompletesNormally_WhenTokenNotCanceled()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		using var cts = new CancellationTokenSource();

		AnalysisResult result = TestWorkspace.CreateCompiler().Analyze(workspace.CreateOptions(), cancellationToken: cts.Token);

		Assert.IsTrue(result.Success, $"Analysis failed: {result.Log}");
		Assert.IsNotNull(result.ScriptModel);
		Assert.IsNotNull(result.FirstLanguageData);
		Assert.HasCount(1, result.AllLanguageData);
		Assert.AreEqual(2, result.FirstLanguageData.TotalSectionStrings, "English.txt must be parsed in full (Test String + Test Level).");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void CanceledCall_WithOptionsThatFailValidation_Throws()
	{
		using var workspace = TestWorkspace.CreateCompilable();
		TRNGCompilerOptions options = workspace.CreateOptions() with { EngineDirectory = workspace.PathOf("missing-engine") };

		using var cts = new CancellationTokenSource();
		cts.Cancel();

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();

		Assert.ThrowsExactly<OperationCanceledException>(() => compiler.Analyze(options, cancellationToken: cts.Token));
		Assert.ThrowsExactly<OperationCanceledException>(() => compiler.Compile(options, cts.Token));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Compile_Throws_AndPublishesNothing_WhenTokenAlreadyCanceled()
	{
		using var workspace = TestWorkspace.CreateCompilable();

		using var cts = new CancellationTokenSource();
		cts.Cancel();

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();

		Assert.ThrowsExactly<OperationCanceledException>(() => compiler.Compile(workspace.CreateOptions(), cts.Token));
		Assert.IsEmpty(Directory.GetFiles(workspace.RootPath, "*.dat"), "A canceled compile publishes nothing to the engine folder.");
		Assert.IsEmpty(Directory.GetFiles(workspace.ScriptDirectory, "*.dat"), "A canceled compile publishes nothing to the script folder.");
	}
}
