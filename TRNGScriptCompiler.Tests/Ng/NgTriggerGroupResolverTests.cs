using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Plugins;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
public sealed class NgTriggerGroupResolverTests
{
	private const string Undeclared = "' which is not declared in any Plugin= command; declare it with a Plugin= line in [Options]";

	private static NgTriggerGroupResolver CreateResolver(UnitRun unit) => new(unit.Run.Diagnostics, unit.Plugins, UnitRun.CommandCatalog.Definitions);

	private static NgArgumentValue Long(int value) => NgArgumentValue.ForInt32(NgArgumentType.Long, value);

	// The parsed arguments of TriggerGroup= 1, first, 406, 1, followed by the array of the values after them.
	private static List<NgArgumentValue> TriggerGroup(int first, params int[] rest)
	{
		var id = NgArgumentValue.ForInt32(NgArgumentType.Word, 1);
		var array = NgArgumentValue.ForArray(NgArgumentType.ArrayLong, rest.Select(Long));

		return [id, Long(first), Long(406), Long(1), array];
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("$2000", false)]
	[DataRow("$32000", true)]
	[DataRow("$FFFF", false)]
	[DataRow("$10000", true)]
	[DataRow("406", false)]
	[DataRow("SOME_CONSTANT", false)]
	public void PluginIdLiteral_IsADollarLiteralWithBits16To23Set(string argument, bool carriesPluginId)
	{
		// VB6 keeps the long form only when the first $ literal of an argument has bits 16-23 set (Module1.bas:7302-7327).
		Assert.AreEqual(carriesPluginId, NgTriggerGroupResolver.HasPluginIdLiteral(["1", argument, "406", "1"]));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExportedTriggerPluginId_IsTranslatedToTheIdOfItsPluginLine()
	{
		// VB6 translates the high word of the first value of each triplet through the #DEFINE @plugin table into the ID of the plugin's
		// Plugin= line (Module1.bas:6969-7012, 7558-7574).
		var unit = new UnitRun(pluginScripts: [new PluginScript("Plugin_X", "Plugin_X.script", ["<START_CONSTANTS>", "<END>"])]);
		unit.Plugins.Declare(UnitRun.PluginLine(id: "2", name: "Plugin_X", location: unit.At(2)));
		unit.Plugins.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_X 7", unit.At(3)));

		List<NgArgumentValue> arguments = TriggerGroup(0x72000, 0x72000, 407, 2);

		Assert.IsTrue(CreateResolver(unit).TryConvertPluginIds(arguments, unit.At(4)));

		IReadOnlyList<NgArgumentValue>? array = arguments[4].ArrayValue;

		Assert.AreEqual(0x22000, arguments[1].Int32Value);
		Assert.IsNotNull(array);
		Assert.AreSequenceEqual([0x22000, 407, 2], array.Select(value => value.Int32Value));

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExportedTriggerOfAnUndeclaredPlugin_Fails()
	{
		// VB6 fails an exported trigger whose @plugin define names a plugin no Plugin= line declares (Module1.bas:6993-7004).
		var unit = new UnitRun();
		unit.Plugins.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_Y 7", unit.At(3)));

		Assert.IsFalse(CreateResolver(unit).TryConvertPluginIds(TriggerGroup(0x72000), unit.At(4)));

		DiagnosticAssert.SingleError(unit.Diagnostics, 4, "Exported trigger references plugin 'Plugin_Y" + Undeclared);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExportedTriggersOfOneUndeclaredPlugin_ReportOneErrorForTheLine()
	{
		var unit = new UnitRun();
		unit.Plugins.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_Y 7", unit.At(3)));

		Assert.IsFalse(CreateResolver(unit).TryConvertPluginIds(TriggerGroup(0x72000, 0x72000, 407, 2), unit.At(4)));

		DiagnosticAssert.SingleError(unit.Diagnostics, 4, "Exported trigger references plugin 'Plugin_Y" + Undeclared);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExportedTriggersOfTwoUndeclaredPlugins_ReportAnErrorForEach()
	{
		var unit = new UnitRun();

		unit.Plugins.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_Y 7", unit.At(2)));
		unit.Plugins.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_Z 8", unit.At(3)));

		Assert.IsFalse(CreateResolver(unit).TryConvertPluginIds(TriggerGroup(0x72000, 0x82000, 407, 2), unit.At(4)));

		DiagnosticAssert.Errors(
			unit.Diagnostics,
			(4, "Exported trigger references plugin 'Plugin_Y" + Undeclared),
			(4, "Exported trigger references plugin 'Plugin_Z" + Undeclared)
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExportedTriggerOfAPluginDefinedAt32768OrMore_Fails()
	{
		// PD-96: VB6 holds the high word in an Integer, so the word of such a define ID is negative and the conversion is skipped
		// (Module1.bas:7044, 7551, 7563-7565), which would leave the define ID where the Plugin= ID belongs.
		var unit = new UnitRun(pluginScripts: [new PluginScript("Plugin_X", "Plugin_X.script", ["<START_CONSTANTS>", "<END>"])]);
		unit.Plugins.Declare(UnitRun.PluginLine(id: "2", name: "Plugin_X", location: unit.At(2)));
		unit.Plugins.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_X 32769", unit.At(3)));

		int exported = unchecked((int)0x80012000);

		Assert.IsFalse(CreateResolver(unit).TryConvertPluginIds(TriggerGroup(exported, exported, 407, 2), unit.At(4)));

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			4,
			"TriggerGroup= names plugin 'Plugin_X' by its ID 32769, which passes 32767, where VB6 skips the conversion and keeps the ID as written; give #DEFINE @Plugin_X an ID below 32768"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void HighWordOf32768OrMoreWithoutAPluginDefine_StaysAsWritten()
	{
		// No define names the word, so VB6 and the port both leave it as written (Module1.bas:7551, 7563-7565).
		var unit = new UnitRun();

		int exported = unchecked((int)0x80012000);
		List<NgArgumentValue> arguments = TriggerGroup(exported);

		Assert.IsTrue(CreateResolver(unit).TryConvertPluginIds(arguments, unit.At(4)));

		Assert.AreEqual(exported, arguments[1].Int32Value);
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void WordFormValuePastTheWord_Fails()
	{
		// The word form width-checks each value as a word (Module1.bas:7442), and VB6's check takes -65536 to 65535 (PD-56, PD-79).
		var unit = new UnitRun();
		ScriptCommandLine line = unit.CommandLine(4, "TriggerGroup= 1,70000,406,1");

		NgTriggerGroupResolver resolver = CreateResolver(unit);

		bool found = resolver.TryFindWordForm(
			commandArgs: TriggerGroup(70000),
			wordForm: NgCommandCode.TriggerGroupWord,
			line,
			wordDefinition: out NgCommandDefinition? definition
		);

		Assert.IsFalse(found);
		Assert.IsNull(definition);

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			4,
			"Argument 2 of TriggerGroup= is out of range: 70000 lies outside -32768 to 65535, the values its word holds; TriggerGroup= keeps its long form only for a $ literal with a plugin ID, so write the value as one"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void WordFormValuesInsideTheWord_FindTheWordDefinition()
	{
		var unit = new UnitRun();
		ScriptCommandLine line = unit.CommandLine(4, "TriggerGroup= 1,$2000,406,1");

		NgTriggerGroupResolver resolver = CreateResolver(unit);

		bool found = resolver.TryFindWordForm(
			commandArgs: TriggerGroup(0x2000),
			wordForm: NgCommandCode.TriggerGroupWord,
			line,
			wordDefinition: out NgCommandDefinition? definition
		);

		Assert.IsTrue(found);
		Assert.AreEqual(NgCommandCode.TriggerGroupWord, definition?.Code);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}
}
