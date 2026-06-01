namespace TRNGScriptCompiler.Constants;

/// <summary>Names a <see cref="ConstantKind"/> in a diagnostic.</summary>
internal static class ConstantKindExtensions
{
	/// <summary>Returns the kind with its article, such as <c>a TRNG constant</c>.</summary>
	internal static string Describe(this ConstantKind kind)
	{
		return kind switch
		{
			ConstantKind.Define => "a #DEFINE",
			ConstantKind.Constant => "a TRNG constant",
			ConstantKind.PluginConstant => "a plugin constant",
			ConstantKind.Slot => "a slot",
			ConstantKind.Static => "a static",
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
		};
	}
}
