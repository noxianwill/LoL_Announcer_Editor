using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace LoL_Announcer_Editor
{
    public static class GeneralUtils
    {
        public static string Lower(string input)
        {
            return input.ToLowerInvariant();
        }

        public static string LowerInPlace(string input)
        {
            // Strings are immutable in C#, so just return a lowercased copy
            return input.ToLowerInvariant();
        }

        private static int Char2Int(char input)
        {
            if (input >= '0' && input <= '9')
                return input - '0';
            if (input >= 'A' && input <= 'F')
                return input - 'A' + 10;
            if (input >= 'a' && input <= 'f')
                return input - 'a' + 10;
            Logger.LogBinMalformed();
            return -1;
        }

        public static byte[] Hex2Bytes(string input)
        {
            if (input.Length % 2 != 0)
                throw new ArgumentException("Hex string must have an even length.");
            byte[] output = new byte[input.Length / 2];
            for (int i = 0; i < input.Length; i += 2)
            {
                int high = Char2Int(input[i]);
                int low = Char2Int(input[i + 1]);
                if (high == -1 || low == -1)
                {
                    string errorMsg = $"Invalid hex character at position {i} or {i + 1}.";
                    Logger.LogError(errorMsg);
                    throw new ArgumentException(errorMsg);
                }
                output[i / 2] = (byte)(high * 16 + low);
            }
            return output;
        }

        public static string Bytes2Hex(byte[] input)
        {
            StringBuilder sb = new StringBuilder(input.Length * 2);
            for (int i = 0; i < input.Length; i++)
            {
                sb.Append(input[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static bool CreateDir(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static bool CreateDirs(string dirPath, bool createLast)
        {
            try
            {
                string path = dirPath;
                if (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
                    path = path.Substring(3);
                string[] parts = dirPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                string current = dirPath.StartsWith("\\") ? "\\" : (dirPath.Length >= 2 && dirPath[1] == ':' ? dirPath.Substring(0, 2) + "\\" : "");
                for (int i = 0; i < parts.Length - (createLast ? 0 : 1); i++)
                {
                    current = Path.Combine(current, parts[i]);
                    if (!Directory.Exists(current))
                        Directory.CreateDirectory(current);
                }
                if (createLast && !Directory.Exists(dirPath))
                    Directory.CreateDirectory(dirPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
} 
