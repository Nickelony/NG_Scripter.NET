using System.Collections.Frozen;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Ng;

/// <summary>The single table of the NG commands the parser treats specially, keyed by code.</summary>
/// <remarks>
/// A code the table does not list follows <see cref="NgDuplicateRule.FirstArgument"/> and has no other rule.
/// </remarks>
internal static class NgCommandPolicies
{
	private static readonly NgCommandPolicy s_default = new(NgDuplicateRule.FirstArgument);
	private static readonly NgCommandPolicy s_flag = new(NgDuplicateRule.OnePerSection);

	/// <summary>
	/// The policies by code, after the VB6 duplicate lists, <c>[Title]</c> rule (Module1.bas:5343)
	/// and word form (:7302-7335).
	/// </summary>
	/// <remarks>
	/// The help groups these arrays (help_new_script_command.txt:3252, 2334, 2410);
	/// AnimationSlot= keys on slot and animation, VB6 on slot (PD-63).
	/// </remarks>
	private static readonly FrozenDictionary<NgCommandCode, NgCommandPolicy> s_policies = new Dictionary<NgCommandCode, NgCommandPolicy>
	{
		[NgCommandCode.Snow] = new(NgDuplicateRule.OnePerSection),
		[NgCommandCode.LevelFarView] = new(NgDuplicateRule.OnePerSection),
		[NgCommandCode.FogRange] = new(NgDuplicateRule.OnePerSection),
		[NgCommandCode.WorldFarView] = new(NgDuplicateRule.OnePerSection),
		[NgCommandCode.TextFormat] = new(NgDuplicateRule.OnePerSection),
		[NgCommandCode.Rain] = new(NgDuplicateRule.OnePerSection),
		[NgCommandCode.KeyPad] = new(NgDuplicateRule.OnePerSection),
		[NgCommandCode.Detector] = new(NgDuplicateRule.OnePerSection),
		[NgCommandCode.Settings] = new(NgDuplicateRule.OnePerSection),
		[NgCommandCode.SoundSettings] = new(NgDuplicateRule.OnePerSection),
		[NgCommandCode.Turbo] = new(NgDuplicateRule.OnePerSection),
		[NgCommandCode.DefaultWindowsFont] = new(NgDuplicateRule.OnePerSection, IsTitleOnly: true),
		[NgCommandCode.LogItem] = new(NgDuplicateRule.SecondArgument),
		[NgCommandCode.Parameters] = new(NgDuplicateRule.FirstTwoArguments),
		[NgCommandCode.AnimationSlot] = new(NgDuplicateRule.SlotAndAnimation),
		[NgCommandCode.Animation] = new(NgDuplicateRule.Exempt),
		[NgCommandCode.MirrorEffect] = new(NgDuplicateRule.Exempt),
		[NgCommandCode.Elevator] = new(NgDuplicateRule.Exempt),
		[NgCommandCode.Customize] = new(NgDuplicateRule.Exempt),
		[NgCommandCode.CombineItems] = new(NgDuplicateRule.Exempt),

		[NgCommandCode.TriggerGroup] = new(
			NgDuplicateRule.FirstArgument,
			WordForm: NgCommandCode.TriggerGroupWord,
			Groups: new(1, 3, "exported trigger")
		),

		[NgCommandCode.MultEnvCondition] = new(NgDuplicateRule.FirstArgument, Groups: new(1, 3, "ENV_ condition")),
		[NgCommandCode.Organizer] = new(NgDuplicateRule.FirstArgument, Groups: new(3, 2, "time and TriggerGroup pair")),
	}.ToFrozenDictionary();

	/// <summary>Gets the policy of a command code.</summary>
	/// <returns>The policy the table lists, or the default.</returns>
	internal static NgCommandPolicy Get(NgCommandCode code) => s_policies.GetValueOrDefault(code, s_default);

	/// <summary>Gets the policy of a command definition.</summary>
	/// <returns>The one-per-section policy of a flag, whatever its code; otherwise, the policy of its code.</returns>
	/// <remarks>
	/// VB6 passes -1 for the arguments of a <c>Bool</c> command and compares
	/// it by name (Module1.bas:6866-6942, 7112-7114).
	/// </remarks>
	internal static NgCommandPolicy Get(NgCommandDefinition definition) => definition.IsFlag ? s_flag : Get(definition.Code);
}
