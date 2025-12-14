using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LoL_Announcer_Editor
{
    public class BnkFileEntry
    {
        public uint FileId { get; set; }
        public uint Offset { get; set; }
        public uint Length { get; set; }
        public byte[] Data { get; set; }
    }

    public class BnkFile
    {
        public int Length { get; set; }
        public List<BnkFileEntry> Entries { get; set; } = new List<BnkFileEntry>();
    }

    public class BnkAudioData
    {
        public uint Id { get; set; }
        public uint Length { get; set; }
#pragma warning disable CS8618 // Non-nullable property 'Data' must contain a non-null value when exiting constructor
        public byte[] Data { get; set; } = Array.Empty<byte>();
#pragma warning restore CS8618
    }

    public static class BnkAudioExtractor
    {
        public static WemInformation ParseAudioBnkFile(string bnkPath, List<StringHash> stringHashes)
        {
            if (!File.Exists(bnkPath))
            {
                Defs.EPrintf($"Error: Failed to open \"{bnkPath}\".\n");
                return null;
            }
            var bnkfile = new BnkFile();
            using (var fs = new FileStream(bnkPath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                if (!ParseBnkFileEntries(reader, bnkfile))
                {
                    Defs.EPrintf($"Error: Failed to find the required sections in file \"{bnkPath}\". Make sure to provide the correct file.\n");
                    return null;
                }
            }

            var wemInformation = new WemInformation
            {
                SortedWemDataList = new List<AudioData>()
            };
            foreach (var entry in bnkfile.Entries)
            {
                wemInformation.SortedWemDataList.Add(new AudioData
                {
                    Id = entry.FileId,
                    Length = entry.Length,
                    Data = entry.Data
                });
            }
            wemInformation.SortedWemDataList.Sort((a, b) => a.Id.CompareTo(b.Id));
            wemInformation.GroupedWems = GroupWems(wemInformation.SortedWemDataList, stringHashes);
            return wemInformation;
        }

        private static bool ParseBnkFileEntries(BinaryReader reader, BnkFile bnkfile)
        {
            try
            {
                uint sectionLength = SkipToSection(reader, "DIDX", false, out long didxSectionOffset, out List<string> sectionLog);
                if (sectionLength == 0)
                {
                    return false;
                }
                int entryCount = (int)sectionLength / 12;
                for (int i = 0; i < entryCount; i++)
                {
                    var entry = new BnkFileEntry
                    {
                        FileId = reader.ReadUInt32(),
                        Offset = reader.ReadUInt32(),
                        Length = reader.ReadUInt32()
                    };
                    bnkfile.Entries.Add(entry);
                }
                sectionLength = SkipToSection(reader, "DATA", false, out long dataSectionOffset, out sectionLog);
                if (sectionLength == 0)
                {
                    return false;
                }
                long dataOffset = reader.BaseStream.Position;
                foreach (var entry in bnkfile.Entries)
                {
                    reader.BaseStream.Seek(dataOffset + entry.Offset, SeekOrigin.Begin);
                    entry.Data = reader.ReadBytes((int)entry.Length);
                }
                bnkfile.Length = bnkfile.Entries.Count;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Helper to skip to a section by name (e.g., "DIDX", "DATA")
        private static uint SkipToSection(BinaryReader reader, string name, bool fromBeginning, out long foundSectionOffset, out List<string> sectionLog)
        {
            sectionLog = new List<string>();
            if (fromBeginning)
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
            uint sectionLength = 0;
            foundSectionOffset = -1;
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                long sectionStart = reader.BaseStream.Position;
                byte[] sectionNameBytes = reader.ReadBytes(4);
                if (sectionNameBytes.Length < 4)
                    break;
                string sectionName = Encoding.ASCII.GetString(sectionNameBytes);
                sectionLog.Add($"{sectionName}@0x{sectionStart:X}");
                sectionLength = reader.ReadUInt32();
                if (sectionName == name)
                {
                    foundSectionOffset = sectionStart;
                    return sectionLength;
                }
                reader.BaseStream.Seek(sectionLength, SeekOrigin.Current);
            }
            return 0;
        }

        public static List<StringWithChildren> GroupWems(List<AudioData> wemDataList, List<StringHash> stringHashes)
        {
            // Flat grouping: event name -> list of AudioData
            var groupedWems = new List<StringWithChildren>();
            foreach (var sh in stringHashes)
            {
                var group = new StringWithChildren { String = sh.String };
                foreach (var wem in wemDataList)
                {
                    if (sh.Hash == wem.Id)
                    {
                        group.Children.Add(wem);
                    }
                }
                if (group.Children.Count > 0)
                {
                    groupedWems.Add(group);
                }
            }
            return groupedWems;
        }

        public static BnkAudioData? ExtractWemFromBnk(string bnkPath, uint wemId)
        {
            if (!File.Exists(bnkPath))
                return null;
            var bnkfile = new BnkFile();
            using (var fs = new FileStream(bnkPath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                if (!ParseBnkFileEntries(reader, bnkfile))
                {
                    Defs.EPrintf($"Error: Failed to find the required sections in file \"{bnkPath}\". Make sure to provide the correct file.\n");
                    return null;
                }
            }

            var wemData = bnkfile.Entries.FirstOrDefault(e => e.FileId == wemId);
            if (wemData == null)
                return null;

#pragma warning disable CS8603 // Possible null reference return
            return new BnkAudioData
            {
                Id = wemData.FileId,
                Length = wemData.Length,
                Data = wemData.Data ?? Array.Empty<byte>()
            };
#pragma warning restore CS8603
        }

        public static List<BnkAudioData>? ExtractAllWemsFromBnk(string bnkPath)
        {
            if (!File.Exists(bnkPath))
                return null;
            var bnkfile = new BnkFile();
            using (var fs = new FileStream(bnkPath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                if (!ParseBnkFileEntries(reader, bnkfile))
                {
                    Defs.EPrintf($"Error: Failed to find the required sections in file \"{bnkPath}\". Make sure to provide the correct file.\n");
                    return null;
                }
            }

#pragma warning disable CS8603 // Possible null reference return
            var wemDataList = bnkfile.Entries.Select(e => new BnkAudioData
            {
                Id = e.FileId,
                Length = e.Length,
                Data = e.Data ?? Array.Empty<byte>()
            }).ToList();

            return wemDataList;
#pragma warning restore CS8603
        }
    }
} 