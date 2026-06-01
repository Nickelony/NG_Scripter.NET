using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Model;

[TestClass]
[TestCategory("Fast")]
public sealed class ScriptCommandLineTests
{
	[TestMethod]
	public void Locations_MarkTheCommandAndTheArgument_OrTheWholeLineWithoutThem()
	{
		// VB6 reports an error with its line and the line's text (Module1.bas:5326-5327); the port marks the command or the argument the error names.
		var unit = new UnitRun();

		ScriptCommandLine line = unit.CommandLine(4, "File= 0, English.txt");

		Assert.AreEqual(new SourceLocation(Line: 4, Column: 0, Length: "File=".Length, File: unit.ScriptFile), line.CommandLocation);
		Assert.AreEqual(new SourceLocation(Line: 4, Column: 9, Length: "English.txt".Length, File: unit.ScriptFile), line.ArgumentLocation(1));
		Assert.AreEqual(new SourceLocation(4, File: unit.ScriptFile), line.ArgumentLocation(2));
	}

	[TestMethod]
	public void Locations_CountInTheSourceLine_NotInTheNormalizedOne()
	{
		var unit = new UnitRun();

		ScriptCommandLine line = unit.CommandLine(4, "File= 0, English.txt", sourceText: "\t\tFile=   0,\tEnglish.txt ");

		Assert.AreEqual(new SourceLocation(Line: 4, Column: 2, Length: "File=".Length, File: unit.ScriptFile), line.CommandLocation);
		Assert.AreEqual(new SourceLocation(Line: 4, Column: 13, Length: "English.txt".Length, File: unit.ScriptFile), line.ArgumentLocation(1));
	}

	[TestMethod]
	public void ArgumentLocation_MarksTheArgumentThatRepeatsAnEarlierValue()
	{
		var unit = new UnitRun();

		ScriptCommandLine line = unit.CommandLine(4, "ImportFile= 3, a.bin, 0, 3");

		Assert.AreEqual(new SourceLocation(Line: 4, Column: 12, Length: 1, File: unit.ScriptFile), line.ArgumentLocation(0));
		Assert.AreEqual(new SourceLocation(Line: 4, Column: line.Text.LastIndexOf('3'), Length: 1, File: unit.ScriptFile), line.ArgumentLocation(3));
	}

	[TestMethod]
	public void ArgumentLocation_MarksAnArgumentWhoseSpacesTheNormalizedLineCollapses()
	{
		var unit = new UnitRun();

		ScriptCommandLine line = unit.CommandLine(4, "Name= 0, my name.txt", sourceText: "Name=  0,  my \t name.txt");

		Assert.AreEqual(new SourceLocation(Line: 4, Column: 11, Length: "my \t name.txt".Length, File: unit.ScriptFile), line.ArgumentLocation(1));
	}

	[TestMethod]
	public void ArgumentLocation_OnAContinuedLine_MarksOnlyTheArgumentsOfItsFirstPhysicalLine()
	{
		// The line continues after the second comma, so its first physical line holds the first two arguments only.
		var unit = new UnitRun();

		ScriptCommandLine line = unit.CommandLine(4, "ImportFile= 7, a.bin, 0, 2", sourceText: "ImportFile= 7, a.bin,>");

		Assert.AreEqual(new SourceLocation(Line: 4, Column: 15, Length: "a.bin".Length, File: unit.ScriptFile), line.ArgumentLocation(1));
		Assert.AreEqual(new SourceLocation(4, File: unit.ScriptFile), line.ArgumentLocation(2));
	}
}
