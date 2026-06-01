using System.Runtime.InteropServices;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Encodes the level-names chunk (the pipe-joined level paths stored as Windows-1252 bytes) into the NG header
/// word list.
/// </summary>
/// <remarks>
/// VB6 adds the chunk only when the script is encrypted (<c>Module1.bas:8654-8655</c>); the chunk itself is plain
/// packed bytes.
/// </remarks>
internal static class NgLevelNamesEncoder
{
	/// <summary>Appends the level-names chunk when the script contains level paths.</summary>
	internal static void Encode(List<short> allWords, ScriptModel scriptModel)
	{
		IReadOnlyList<string> levelNames = scriptModel.Parsed.LevelPaths;

		if (levelNames.Count == 0)
			return;

		string joinedLevelNames = string.Join('|', levelNames);

		if ((joinedLevelNames.Length & 1) != 0)
			joinedLevelNames += " ";

		byte[] bytes = Vb6Ansi.GetBytes(joinedLevelNames);
		List<short> payload = [(short)bytes.Length];

		payload.AddPackedBytes(bytes);

		allWords.AddChunk(unchecked((short)NgTagType.LevelNames), CollectionsMarshal.AsSpan(payload));
	}
}
