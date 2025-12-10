using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LoL_Announcer_Editor
{
    public class StringHash
    {
        public required string String { get; set; }
        public uint Hash { get; set; }
        public uint ContainerId { get; set; }
        public uint MusicSegmentId { get; set; }
        public uint SwitchId { get; set; }
        public uint SoundIndex { get; set; }
    }

    public static class BinUtils
    {
        public static uint Fnv1Hash(string input)
        {
            uint hash = 0x811c9dc5;
            foreach (char c in input)
            {
                hash *= 0x01000193;
                uint charValue = (uint)((c > 64 && c < 91) ? c + 32 : c);
                hash ^= charValue;
            }
            return hash;
        }

        private static string? ReadString(byte[] buffer, int offset, out int bytesRead)
        {
            if (buffer.Length - offset < 2)
            {
                bytesRead = 0;
                return null;
            }
            ushort stringLength = BitConverter.ToUInt16(buffer, offset);
            if (buffer.Length - offset < 2 + stringLength)
            {
                bytesRead = 0;
                return null;
            }
            char[] chars = new char[stringLength];
            for (int i = 0; i < stringLength; i++)
            {
                chars[i] = (char)buffer[offset + 2 + i];
            }
            string result = new string(chars);
            bytesRead = 2 + stringLength;
            return result;
        }

        // Helper to find a byte pattern in a byte array
        private static int IndexOf(byte[] haystack, byte[] needle, int start = 0)
        {
            for (int i = start; i <= haystack.Length - needle.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        public static List<StringHash>? ParseBinFile(string binPath)
        {
            if (!File.Exists(binPath))
            {
                Defs.EPrintf($"Error: Failed to open \"{binPath}\".\n");
                return null;
            }
            byte[] buffer = File.ReadAllBytes(binPath);
            var savedStrings = new List<StringHash>();

            // Search for 'events' containers
            {
                byte[] toSearch = { 0x84, 0xE3, 0xD8, 0x12, 0x80, 0x10 };
                int currentPosition = 0;
                while (true)
                {
                    int found = IndexOf(buffer, toSearch, currentPosition);
                    if (found == -1) break;
                    currentPosition = found + toSearch.Length;
                    if (buffer.Length - currentPosition < 8) break;
                    currentPosition += 4; // skip object size
                    uint amount = BitConverter.ToUInt32(buffer, currentPosition);
                    currentPosition += 4;
                    Defs.DPrintf($"event amount: {amount}\n");
                    for (uint i = 0; i < amount; i++)
                    {
                        int bytesRead;
                        string? str = ReadString(buffer, currentPosition, out bytesRead);
                        if (str == null) goto error;
                        var newPair = new StringHash { String = str!, Hash = Fnv1Hash(str!) };
                        savedStrings.Add(newPair);
                        currentPosition += bytesRead;
                    }
                }
            }

            // Search for 'music' embedded objects
            {
                byte[] toSearch = { 0xD4, 0x4F, 0x9C, 0x9F, 0x83 };
                int currentPosition = 0;
                while (true)
                {
                    int found = IndexOf(buffer, toSearch, currentPosition);
                    if (found == -1) break;
                    currentPosition = found + toSearch.Length;
                    if (buffer.Length - currentPosition < 4) break;
                    uint typeHash = BitConverter.ToUInt32(buffer, currentPosition);
                    currentPosition += 4;
                    if (typeHash == 0) continue;
                    if (buffer.Length - currentPosition < 6) break;
                    currentPosition += 4; // skip object size
                    ushort amount = BitConverter.ToUInt16(buffer, currentPosition);
                    currentPosition += 2;
                    Defs.DPrintf($"music amount: {amount}\n");
                    for (ushort i = 0; i < amount; i++)
                    {
                        if (buffer.Length - currentPosition < 5) break;
                        currentPosition += 4; // skip name (hash)
                        byte binType = buffer[currentPosition];
                        if (binType != 0x10) goto error;
                        currentPosition++;
                        int bytesRead;
                        string? str = ReadString(buffer, currentPosition, out bytesRead);
                        if (str == null) goto error;
                        var newPair = new StringHash { String = str!, Hash = Fnv1Hash(str!) };
                        savedStrings.Add(newPair);
                        currentPosition += bytesRead;
                    }
                }
            }

            foreach (var sh in savedStrings)
            {
                Defs.DPrintf($"string: \"{sh.String}\".\n");
            }
            return savedStrings;

        error:
            Defs.EPrintf("Error: Malformed/truncated bin file!\n");
            return null;
        }
    }
} 