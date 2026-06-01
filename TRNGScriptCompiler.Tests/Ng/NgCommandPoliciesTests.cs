using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
[TestCategory("Fast")]
public sealed class NgCommandPoliciesTests
{
	[TestMethod]
	public void DefaultWindowsFont_IsTheOnlyTitleCommand()
	{
		// VB6 fails DefaultWindowsFont= outside [Title] and no other command (Module1.bas:5343-5352).
		Assert.IsTrue(NgCommandPolicies.Get(NgCommandCode.DefaultWindowsFont).IsTitleOnly);
		Assert.IsFalse(NgCommandPolicies.Get(NgCommandCode.Snow).IsTitleOnly);
	}

	[TestMethod]
	public void TriggerGroup_HasAWordForm()
	{
		// VB6 writes TriggerGroup= as code 46 when no argument carries a plugin ID (Module1.bas:7302-7335).
		Assert.AreEqual(NgCommandCode.TriggerGroupWord, NgCommandPolicies.Get(NgCommandCode.TriggerGroup).WordForm);
		Assert.IsNull(NgCommandPolicies.Get(NgCommandCode.MultEnvCondition).WordForm);
	}

	[TestMethod]
	[DataRow(21, 1, 3, "exported trigger")]
	[DataRow(19, 1, 3, "ENV_ condition")]
	[DataRow(23, 3, 2, "time and TriggerGroup pair")]
	public void GroupedCommands_NameWhereTheirGroupsStart(int code, int firstValue, int size, string name)
	{
		// The help groups the values of TriggerGroup=, MultEnvCondition= and Organizer=, which neither compiler checks (Module1.bas:7154-7172)
		// (PD-57).
		Assert.AreEqual<NgArgumentGroups?>(new NgArgumentGroups(firstValue, size, name), NgCommandPolicies.Get((NgCommandCode)code).Groups);
	}

	[TestMethod]
	public void UngroupedCommand_HasNoGroups()
	{
		// VB6 reads the values of any other array one by one, with no group (Module1.bas:7388-7401).
		Assert.IsNull(NgCommandPolicies.Get(NgCommandCode.Animation).Groups);
	}
}
