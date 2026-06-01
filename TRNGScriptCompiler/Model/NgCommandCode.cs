namespace TRNGScriptCompiler.Model;

/// <summary>
/// Identifies the NG command codes the compiler treats specially; the constants file may define further codes.
/// </summary>
/// <remarks>
/// The values match the VB6 <c>Tipo_CodiceNewTag</c> enum and the code column of the command table
/// in <c>scripter_constants.txt</c>.
/// </remarks>
internal enum NgCommandCode
{
	/// <summary>
	/// The code of a <c>Bool</c> command, which sets a flag of its section instead of writing a command.
	/// </summary>
	Flag = 0,

	AssignSlot = 1,
	Snow = 2,
	LevelFarView = 3,
	FogRange = 4,
	WorldFarView = 5,
	TextFormat = 6,
	Rain = 7,
	Animation = 10,
	MirrorEffect = 11,
	Elevator = 12,
	KeyPad = 13,
	Detector = 15,
	Settings = 16,
	MultEnvCondition = 19,
	Customize = 20,
	TriggerGroup = 21,
	GlobalTrigger = 22,
	Organizer = 23,
	SoundSettings = 24,
	Parameters = 27,
	Turbo = 28,
	TestPosition = 30,
	LogItem = 31,
	Image = 34,
	CombineItems = 38,
	AnimationSlot = 40,

	/// <summary>The <c>DefaultWindowsFont=</c> command (allowed only in <c>[Title]</c>).</summary>
	DefaultWindowsFont = 41,

	Plugin = 43,

	/// <summary>The word-sized variant of <c>TriggerGroup=</c> (code 46), declared by the constants table.</summary>
	TriggerGroupWord = 46,

	/// <summary>
	/// Pseudo-code (reserved as 200 in the constants table) marking the <c>[Options]</c> flags word chunk written
	/// by the NG header encoder.
	/// </summary>
	FlagsOption = 200,

	/// <summary>
	/// Pseudo-code (reserved as 201 in the constants table) marking the <c>[Level]</c> flags word chunk written
	/// by the NG header encoder.
	/// </summary>
	FlagsLevel = 201,
}
