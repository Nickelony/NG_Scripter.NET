using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
public sealed class OptionsSectionBuilderTests
{
	private static (OptionsSectionBuilder Builder, UnitRun Unit) CreateBuilder()
	{
		var unit = new UnitRun();

		return (
			new OptionsSectionBuilder(
				run: unit.Run,
				evaluator: unit.Evaluator,
				ngCommandNames: UnitRun.NgCommandNames,
				engineDirectoryPath: unit.Root
			),
			unit
		);
	}

	private static (OptionsSectionBuilder Builder, UnitRun Unit) Add(params string[] lines)
	{
		(OptionsSectionBuilder builder, UnitRun unit) = CreateBuilder();

		for (int i = 0; i < lines.Length; i++)
			builder.TryAdd(unit.CommandLine(i + 2, lines[i]));

		return (builder, unit);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void EnabledOptions_SetTheirFlags_AndDisabledOnesLeaveThemClear()
	{
		// VB6 sets the flag of an option only when it reads ENABLED (Module1.bas:5402-5425).
		(OptionsSectionBuilder builder, UnitRun unit) = Add("LoadSave= ENABLED", "PlayAnyLevel= ENABLED", "FlyCheat= DISABLED", "demodisc= enabled");

		Assert.AreEqual(ScriptMainFlags.LoadSave | ScriptMainFlags.PlayAnyLevel | ScriptMainFlags.DemoDisc, builder.Options.Flags);
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TitleDisabled_ClearsTheTitleFlag()
	{
		// VB6 sets smf_Title for Title= ENABLED and its ignore flag for DISABLED (Module1.bas:5406-5411); the port keeps the flag alone (PD-54).
		(OptionsSectionBuilder enabled, _) = Add("Title= ENABLED");
		(OptionsSectionBuilder disabled, _) = Add("Title= DISABLED");

		Assert.AreEqual(ScriptMainFlags.Title, enabled.Options.Flags);
		Assert.AreEqual(ScriptMainFlags.None, disabled.Options.Flags);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void NumericOptions_ReadTheVb6NumericForms()
	{
		// VB6 reads InputTimeOut= as a dword and Security= as a byte with GetArgNumerico (Module1.bas:5426-5430).
		(OptionsSectionBuilder builder, UnitRun unit) = Add("InputTimeOut= $10+CL_RED", "Security= 255");

		Assert.AreEqual((19, 255), (builder.Options.InputTimeOut, builder.Options.Security));
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("Security= 256", "The value of 'Security=' must be between 0 and 255: 256")]
	[DataRow("Security= -1", "The value of 'Security=' must be between 0 and 255: -1")]
	[DataRow("InputTimeOut= 1,2", "Wrong number of arguments for 'InputTimeOut=': expected 1, got 2")]
	[DataRow("InputTimeOut= SOON", "The value of 'InputTimeOut=' is invalid: SOON is neither a number nor a known name")]
	[DataRow("LoadSave= YES", "Argument 1 of LoadSave= is invalid: YES is neither ENABLED nor DISABLED")]
	[DataRow("Title= YES", "Argument 1 of Title= is invalid: YES is neither ENABLED nor DISABLED")]
	[DataRow("LoadSave= ENABLED,1", "Wrong number of arguments for 'LoadSave=': expected 1, got 2")]
	[DataRow("FooBar= 1", "Unknown command for [Options] section: FooBar=; correct its name or remove the line")]
	public void InvalidOption_Fails(string line, string expectedError)
	{
		// VB6 stores Security= in a Byte, which overflows outside 0 to 255 (Module1.bas:5430, 6671), fails a value it cannot read
		// (:5465-5469) and a command it lacks (:5436-5440), and ignores extra arguments, which the port fails (PD-57).
		(OptionsSectionBuilder builder, UnitRun unit) = CreateBuilder();

		Assert.IsFalse(builder.TryAdd(unit.CommandLine(2, line)));
		DiagnosticAssert.SingleError(unit.Diagnostics, 2, expectedError);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void RepeatedOption_FailsOnADifferentValue_AndWarnsOnTheSameOne()
	{
		// VB6 only ever sets a flag and keeps the last number (Module1.bas:5402-5430), so a second value is lost without a word; the port
		// fails a different one and warns on an identical one (PD-31).
		(OptionsSectionBuilder builder, UnitRun unit) = CreateBuilder();

		Assert.IsTrue(builder.TryAdd(unit.CommandLine(2, "LoadSave= ENABLED")));
		Assert.IsTrue(builder.TryAdd(unit.CommandLine(3, "loadsave= enabled")));
		Assert.IsTrue(builder.TryAdd(unit.CommandLine(4, "Security= $10")));
		Assert.IsFalse(builder.TryAdd(unit.CommandLine(5, "Security= 17")));

		DiagnosticAssert.HasWarning(unit.Diagnostics, 3, "'loadsave=' repeats line 2 with the same value; remove one of the two lines");

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			5,
			"'Security=' gives a value different from line 4, and the setting holds one value; keep one of the two lines"
		);

		Assert.AreEqual(16, builder.Options.Security);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void NgCommand_IsCollectedInSourceOrder()
	{
		// VB6 appends the NG lines of every [Options] section to one list (Module1.bas:5448-5456).
		(OptionsSectionBuilder builder, UnitRun unit) = Add("Plugin= 1, Plugin_One, 0", "LoadSave= ENABLED", "Settings= 8");

		Assert.AreSequenceEqual(
			[("Plugin=", 2), ("Settings=", 4)],
			builder.NgLines.Select(line => (line.CommandLine.Command, line.CommandLine.Location.Line))
		);

		Assert.AreEqual(ScriptMainFlags.LoadSave, builder.Options.Flags);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void UnknownOptionsCommand_Fails()
	{
		const string script = TestScripts.Language + "[Options]\nBogus= 1\n" + TestScripts.Level;

		using var workspace = TestWorkspace.CreateCompilable(script, TestWorkspace.CompilableLanguageContent);
		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "VB6 treats unknown [Options] commands as fatal errors.");
		DiagnosticAssert.SingleError(result.Diagnostics, 4, "Unknown command for [Options] section: Bogus=; correct its name or remove the line");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void InvalidEnabledValue_Fails()
	{
		const string script = TestScripts.Language + "[Options]\nTitle= DISSABLED\n" + TestScripts.Level;

		using var workspace = TestWorkspace.CreateCompilable(script, TestWorkspace.CompilableLanguageContent);
		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "VB6 rejects values other than ENABLED/DISABLED.");
		DiagnosticAssert.SingleError(result.Diagnostics, 4, "Argument 1 of Title= is invalid: DISSABLED is neither ENABLED nor DISABLED");
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Security= 256", "The value of 'Security=' must be between 0 and 255: 256")]
	[DataRow("Security= -1", "The value of 'Security=' must be between 0 and 255: -1")]
	[DataRow("Settings= 256", "The value of 'Settings=' must be between 0 and 255: 256")]
	[DataRow("Security= Five", "The value of 'Security=' is invalid: Five is neither a number nor a known name")]
	[DataRow("InputTimeOut= Five", "The value of 'InputTimeOut=' is invalid: Five is neither a number nor a known name")]
	[DataRow("Security= 1,2", "Wrong number of arguments for 'Security=': expected 1, got 2")]
	[DataRow("InputTimeOut= 10,20", "Wrong number of arguments for 'InputTimeOut=': expected 1, got 2")]
	public void OptionsValue_OutOfRangeUnparsableOrWithExtraArguments_Fails(string optionsLine, string message)
	{
		// Security= and Settings= are stored in VB6 Byte variables, which overflow outside 0-255 (Module1.bas:6671, 8008), and
		// an InputTimeOut= or Security= value that does not parse aborts (Module1.bas:5426-5430). VB6 ignores extra
		// arguments to both; PD-57 rejects them.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Options]\n" + optionsLine + "\n" + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		DiagnosticAssert.SingleError(result.Diagnostics, 4, message);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OptionsValues_AtTheTopOfTheByteRange_Compile()
	{
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nSecurity= 255\nSettings= $F7\nInputTimeOut= 100000\n" + TestScripts.Level
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		var header = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat"));

		Assert.AreEqual(255, header.Security);
		Assert.AreEqual(100000, header.InputTimeOut);
	}
}
