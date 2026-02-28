using System.Text;

namespace TRNGScriptCompiler.Utilities;

public static class BinaryUtilities
{
    /// <summary>
    /// Reads a null-terminated string from a binary reader.
    /// </summary>
    public static string ReadNullTerminatedString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        byte b;

        while ((b = reader.ReadByte()) != 0)
            bytes.Add(b);

        return Encoding.GetEncoding(1252).GetString([.. bytes]);
    }

    /// <summary>
    /// Writes a null-terminated string to a binary writer.
    /// </summary>
    public static void WriteNullTerminatedString(BinaryWriter writer, string text)
    {
        byte[] bytes = Encoding.GetEncoding(1252).GetBytes(text);

        writer.Write(bytes);
        writer.Write((byte)0);
    }

    /// <summary>
    /// Adds a word (16-bit) to a byte array.
    /// </summary>
    public static void AddWord(List<byte> target, short value)
    {
        target.Add((byte)(value & 0xFF));
        target.Add((byte)((value >> 8) & 0xFF));
    }

    /// <summary>
    /// Adds a dword (32-bit) to a byte array.
    /// </summary>
    public static void AddDWord(List<byte> target, int value)
    {
        target.Add((byte)(value & 0xFF));
        target.Add((byte)((value >> 8) & 0xFF));
        target.Add((byte)((value >> 16) & 0xFF));
        target.Add((byte)((value >> 24) & 0xFF));
    }
}
