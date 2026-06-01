using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Plugins;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Plugins;

[TestClass]
[TestCategory("Fast")]
public sealed class PluginRegistryTests
{
	private static readonly string s_file = Path.GetFullPath("Script.txt");

	private static (PluginRegistry Registry, DiagnosticSink Diagnostics, CompilerLog Logger) CreateRegistry()
	{
		// The run's engine folder is absent, so a plugin's .dll lookup lists no folder.
		var run = new UnitRun();
		return (run.Plugins, run.Run.Diagnostics, run.Run.Logger);
	}

	private static SourceLocation Line(int line) => new(line, File: s_file);

	private static int Translate(PluginRegistry registry, int defineId)
	{
		Assert.IsTrue(registry.TryTranslateTriggerPluginId(defineId, out int pluginId, out _));
		return pluginId;
	}

	private static void Declare(PluginRegistry registry, string id, string name, int line) =>
		registry.Declare(UnitRun.PluginLine(id, name, Line(line)));

	[TestMethod]
	public void DefinedId_TranslatesToTheIdOfThePluginLine()
	{
		// VB6 finds the plugin by its define ID, then the ID of its Plugin= line (Module1.bas:6969-7012).
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();
		Declare(registry, id: "3", name: "Plugin_A", line: 1);

		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_A 42", Line(2)));

		Assert.AreEqual(3, Translate(registry, 42));
		Assert.AreEqual(7, Translate(registry, 7), "An ID that no define names stays as written.");
		Assert.DoesNotContain(diagnostic => diagnostic.IsError, diagnostics.Recorded);
	}

	[TestMethod]
	public void DefinedPluginWithoutAPluginLine_FailsWithoutReporting()
	{
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();
		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_A 42", Line(1)));

		bool translated = registry.TryTranslateTriggerPluginId(42, out int pluginId, out string? undeclaredPlugin);

		Assert.AreEqual((false, 42, "Plugin_A"), (translated, pluginId, undeclaredPlugin));
		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);
	}

	[TestMethod]
	public void DuplicateDefineId_Fails()
	{
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();

		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_A 42", Line(1)));
		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_B 42", Line(2)));

		DiagnosticAssert.SingleError(
			diagnostics.Recorded,
			2,
			"The plugin ID 42 is already defined for another plugin; add a [#DEFINE @plugins clear] line to clear the previous definitions"
		);
	}

	[TestMethod]
	public void IdOfAnEntryBeforeTheEntryOfTheName_Fails()
	{
		// VB6 walks the table and stops at the entry of the same name, so an earlier entry with the new ID is fatal even when the name
		// exists further on (Module1.bas:13601-13615).
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();

		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_B 2", Line(1)));
		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_A 1", Line(2)));
		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_A 2", Line(3)));

		CompilerDiagnostic diagnostic = diagnostics.Recorded.Single();

		Assert.IsTrue(diagnostic.IsError);
		Assert.AreEqual(3, diagnostic.LineNumber);
	}

	[TestMethod]
	public void IdOfAnEntryAfterTheEntryOfTheName_IsReplacedWithAWarning()
	{
		// The walk stops at the entry of the name before it reaches the later entry with the same ID (Module1.bas:13603-13607).
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();
		Declare(registry, id: "5", name: "Plugin_A", line: 1);

		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_A 1", Line(2)));
		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_B 2", Line(3)));
		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_A 2", Line(4)));

		Assert.ContainsSingle(diagnostic => diagnostic.LineNumber == 4, diagnostics.Recorded);

		DiagnosticAssert.HasWarning(
			diagnostics.Recorded,
			4,
			"Same plugin name [Plugin_A] had already been defined; add a [#DEFINE @plugins clear] line to clear the previous definition"
		);

		Assert.AreEqual(5, Translate(registry, 2), "The first entry with ID 2 is Plugin_A, as in VB6.");
	}

	[TestMethod]
	public void DuplicateDefineName_WarnsAndReplacesTheId()
	{
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();
		Declare(registry, id: "5", name: "Plugin_A", line: 1);

		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_A 42", Line(2)));
		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_A 77", Line(3)));

		CompilerDiagnostic warning = DiagnosticAssert.HasWarning(
			diagnostics.Recorded,
			3,
			"Same plugin name [Plugin_A] had already been defined; add a [#DEFINE @plugins clear] line to clear the previous definition"
		);

		// "#DEFINE " is 8 characters long, and "@Plugin_A" is 9.
		Assert.AreEqual((8, 9), (warning.Column, warning.Length));

		Assert.AreEqual(5, Translate(registry, 77));
		Assert.AreEqual(42, Translate(registry, 42), "The replacement releases the previous ID.");
		Assert.DoesNotContain(diagnostic => diagnostic.IsError, diagnostics.Recorded);
	}

	[TestMethod]
	public void ClearDirective_EmptiesTheRegistry()
	{
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();
		Declare(registry, id: "5", name: "Plugin_A", line: 1);

		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_A 42", Line(2)));
		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @plugins clear", Line(3)));

		Assert.AreEqual(42, Translate(registry, 42));
		Assert.DoesNotContain(diagnostic => diagnostic.IsError, diagnostics.Recorded);
	}

	[TestMethod]
	public void ClearWithWrongName_Fails()
	{
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();

		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @pluginx clear", Line(6)));

		DiagnosticAssert.SingleError(diagnostics.Recorded, 6, "Wrong syntax for clearing the plugin table; write #DEFINE @plugins clear");
	}

	[TestMethod]
	public void DefineWithoutAValue_Fails()
	{
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();

		registry.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_A", Line(6)));

		DiagnosticAssert.SingleError(diagnostics.Recorded, 6, "Wrong number of arguments for '#DEFINE': expected 2, got 1");
	}

	[TestMethod]
	[DataRow("2", 2)]
	[DataRow("$2", 2)]
	[DataRow("#2", 2)]
	[DataRow("&H2", 2)]
	public void PluginLineId_IsReadAsTheNumberItWrites(string id, int expected)
	{
		// VB6 reads the ID of its plugin table with GetCampoNumero, which takes $ and # hexadecimal (Module1.bas:2685-2702, 15082), so
		// Plugin= $2,Plugin_X,IGNORE registers the plugin under the ID the command writes.
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();

		Declare(registry, id, name: "Plugin_X", line: 1);

		Assert.IsTrue(registry.TryGetDeclaredId("plugin_x", out int pluginId));
		Assert.AreEqual(expected, pluginId);
		Assert.DoesNotContain(diagnostic => diagnostic.IsError, diagnostics.Recorded);
	}

	[TestMethod]
	public void PluginLineIdThatIsNoNumber_Fails()
	{
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();

		Declare(registry, id: "MY_ID", name: "Plugin_X", line: 4);

		CompilerDiagnostic diagnostic = diagnostics.Recorded.Single(diagnostic => diagnostic.IsError);

		Assert.AreEqual("The plugin ID 'MY_ID' of Plugin= is not a number; write the ID as a decimal or $ number", diagnostic.Message);

		Assert.IsFalse(registry.TryGetDeclaredId("Plugin_X", out _));
	}

	[TestMethod]
	public void PluginLineIdOutOfRange_FailsWithTheReasonItDoesNotFit()
	{
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();

		Declare(registry, id: "4294967296", name: "Plugin_X", line: 4);

		DiagnosticAssert.SingleError(
			diagnostics.Recorded,
			line: 4,
			message: "The plugin ID '4294967296' of Plugin= is invalid: 4294967296 does not fit 32 bits"
		);

		Assert.IsFalse(registry.TryGetDeclaredId("Plugin_X", out _));
	}

	[TestMethod]
	[DataRow("MY_ID", "The plugin ID 'MY_ID' of #DEFINE @plugin is not a number; write the ID as a decimal or $ number")]
	[DataRow("4294967296", "The plugin ID '4294967296' of #DEFINE @plugin is invalid: 4294967296 does not fit 32 bits")]
	public void DefineIdThatIsNoPluginId_FailsWithTheWordingOfAPluginLine(string id, string message)
	{
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();

		registry.Defines.Apply(UnitRun.PluginDefineLine($"#DEFINE @Plugin_A {id}", Line(4)));

		DiagnosticAssert.SingleError(diagnostics.Recorded, line: 4, message);
	}

	[TestMethod]
	public void OnePluginUnderTwoIds_Fails()
	{
		// VB6 writes both lines, and its trigger table takes the first ID (Module1.bas:6993-6995).
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();

		Declare(registry, id: "1", name: "Plugin_X", line: 1);
		Declare(registry, id: "2", name: "Plugin_X", line: 2);

		CompilerDiagnostic diagnostic = diagnostics.Recorded.Single(diagnostic => diagnostic.IsError);

		Assert.AreEqual(
			"Plugin= declares Plugin_X with ID 2, but line 1 declares it with ID 1, and a plugin takes one ID; keep one of the two lines",
			diagnostic.Message
		);

		Assert.AreEqual(2, diagnostic.LineNumber);
	}

	[TestMethod]
	public void PluginLineRepeatedWithItsId_DeclaresThePluginOnce()
	{
		(PluginRegistry registry, DiagnosticSink diagnostics, _) = CreateRegistry();

		Declare(registry, id: "1", name: "Plugin_X", line: 1);
		Declare(registry, id: "1", name: "Plugin_X", line: 2);

		Assert.HasCount(1, registry.Declarations);
		Assert.DoesNotContain(diagnostic => diagnostic.IsError, diagnostics.Recorded, "The NG duplicate rule reports the repeated ID.");
	}
}
