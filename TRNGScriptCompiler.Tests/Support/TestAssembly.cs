// Each test writes below its own temp directory and builds its own compiler, and the library holds no mutable static state,
// so any two tests may run at once.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace TRNGScriptCompiler.Tests.Support;

/// <summary>Holds the cleanup of the test assembly, which removes the temp root that its tests write under.</summary>
[TestClass]
public sealed class TestAssembly
{
	/// <summary>Removes this run's temp root after all its tests have run.</summary>
	[AssemblyCleanup]
	public static void RemoveTempRoot()
	{
		try
		{
			if (Directory.Exists(TestWorkspace.TempRoot))
				Directory.Delete(TestWorkspace.TempRoot, true);
		}
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}
}
