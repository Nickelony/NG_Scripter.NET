namespace TRNGScriptCompiler.Kernel.Runtime;

/// <summary>The names of the files the compiler reads and writes by convention.</summary>
internal static class CompilerFileNames
{
	/// <summary>The file name of the TRNG constants file that defines NG command signatures.</summary>
	internal const string ScripterConstants = "scripter_constants.txt";

	/// <summary>The script the script folder holds.</summary>
	internal const string ScriptTxt = "Script.txt";

	/// <summary>The compiled script the engine folder receives.</summary>
	internal const string ScriptDat = "Script.dat";

	/// <summary>The slot and static names of the engine folder.</summary>
	internal const string ObjectsH = "Objects.h";
}
