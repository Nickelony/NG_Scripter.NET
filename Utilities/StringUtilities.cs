using System.Text;

namespace TRNGScriptCompiler.Utilities;

public static class StringUtilities
{
    /// <summary>
    /// Converts C-style escape codes (\n, \t, \xNN) to actual characters.
    /// </summary>
    public static bool ConvertCStyleToActual(ref string text, out string specialTag, bool forNGStrings = false)
    {
        specialTag = string.Empty;

        // VB6 Logic: Trim BEFORE removing comments
        text = text.Trim(' ');

        // Remove comments
        int commentIndex = text.IndexOf(';');

        if (commentIndex >= 0)
            text = text[..commentIndex];

        if (string.IsNullOrEmpty(text))
            return true;

        var result = new StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                char nextChar = text[i + 1];

                if (nextChar == 'n')
                {
                    result.Append('\n');
                    i++;
                }
                else if (nextChar == 't')
                {
                    result.Append('\t');
                    i++;
                }
                else if (nextChar == 'x' && i + 3 < text.Length)
                {
                    // \xNN format - hexadecimal byte
                    string hexValue = text.Substring(i + 2, 2);

                    if (IsHexadecimal(hexValue))
                    {
                        byte byteValue = Convert.ToByte(hexValue, 16);
                        result.Append((char)byteValue);
                        i += 3;
                    }
                    else
                    {
                        result.Append(text[i]);
                    }
                }
                else if (nextChar == '\\')
                {
                    result.Append('\\');
                    i++;
                }
                else
                {
                    result.Append(text[i]);
                }
            }
            else
            {
                result.Append(text[i]);
            }
        }

        text = result.ToString();

        // Handle special tags (for non-ExtraNG strings only)
        if (!forNGStrings)
        {
            int colonIndex = text.IndexOf(':');

            if (colonIndex > 0)
            {
                // Check if there are no spaces before the colon
                bool hasSpaces = false;

                for (int i = 0; i < colonIndex; i++)
                {
                    if (text[i] == ' ')
                    {
                        hasSpaces = true;
                        break;
                    }
                }

                if (!hasSpaces)
                {
                    // Extract special tag (including the colon)
                    specialTag = text[..(colonIndex + 1)];

                    // Remove tag from text
                    text = text[(colonIndex + 1)..].Trim(' ');
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Converts actual characters to C-style escape codes.
    /// </summary>
    public static string ConvertActualToCStyle(string text)
    {
        var result = new StringBuilder();

        foreach (char c in text)
        {
            if (c == '\n')
                result.Append("\\n");
            else if (c == '\t')
                result.Append("\\t");
            else if (c is (char)0x17 or (char)0x14)
                result.Append($"\\x{(int)c:X2}");
            else
                result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>
    /// Encrypts/Decrypts language strings using XOR 0xA5.
    /// </summary>
    public static byte[] EncryptLanguageString(string text)
    {
        byte[] bytes = Encoding.GetEncoding(1252).GetBytes(text);

        for (int i = 0; i < bytes.Length; i++)
            bytes[i] ^= 0xA5;

        return bytes;
    }

    /// <summary>
    /// Decrypts language strings using XOR 0xA5.
    /// </summary>
    public static string DecryptLanguageString(string text)
    {
        byte[] bytes = Encoding.GetEncoding(1252).GetBytes(text);

        for (int i = 0; i < bytes.Length; i++)
            bytes[i] ^= 0xA5;

        return Encoding.GetEncoding(1252).GetString(bytes);
    }

    /// <summary>
    /// Normalizes a line by trimming and removing comments.
    /// </summary>
    public static string NormalizeLine(string line)
    {
        // Remove comments (everything after ;)
        int commentIndex = line.IndexOf(';');

        if (commentIndex >= 0)
            line = line[..commentIndex];

        return line.Trim();
    }

    /// <summary>
    /// Extracts command and arguments from a line.
    /// </summary>
    public static (string command, List<string> arguments) ParseCommandLine(string line)
    {
        int equalsIndex = line.IndexOf('=');

        if (equalsIndex < 0)
            return (string.Empty, new List<string>());

        string command = line[..(equalsIndex + 1)].Trim();
        string argsText = line[(equalsIndex + 1)..].Trim();

        // Split by comma, but be careful with strings
        var arguments = new List<string>();
        var currentArg = new StringBuilder();
        bool inQuotes = false;

        foreach (char c in argsText)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                arguments.Add(currentArg.ToString().Trim());
                currentArg.Clear();
            }
            else
            {
                currentArg.Append(c);
            }
        }

        if (currentArg.Length > 0)
            arguments.Add(currentArg.ToString().Trim());

        return (command, arguments);
    }

    /// <summary>
    /// Checks if a string is hexadecimal.
    /// </summary>
    public static bool IsHexadecimal(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (char c in text)
        {
            if (c is not ((>= '0' and <= '9')
                  or (>= 'a' and <= 'f')
                  or (>= 'A' and <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Converts a value to a formatted hex string.
    /// </summary>
    public static string FormatHex(long value, int digits)
        => value.ToString($"X{digits}");

    /// <summary>
    /// Gets filename without path.
    /// </summary>
    public static string GetFileName(string path)
        => Path.GetFileName(path);

    /// <summary>
    /// Gets filename without extension.
    /// </summary>
    public static string GetFileNameWithoutExtension(string path)
        => Path.GetFileNameWithoutExtension(path);

    /// <summary>
    /// Gets directory from path.
    /// </summary>
    public static string GetDirectory(string path)
        => Path.GetDirectoryName(path) ?? string.Empty;

    /// <summary>
    /// Changes file extension.
    /// </summary>
    public static string ChangeExtension(string path, string newExtension)
        => Path.ChangeExtension(path, newExtension);
}
