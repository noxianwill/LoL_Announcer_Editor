using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LoL_Announcer_Editor
{
    public class WpkFileEntry
    {
        public uint DataOffset { get; set; }
        public uint DataLength { get; set; }
        public required string Filename { get; set; }
        public required byte[] Data { get; set; }
    }

    public class WpkFile
    {
        public required byte[] Magic { get; set; }
        public uint Version { get; set; }
        public uint FileCount { get; set; }
        public uint OffsetAmount { get; set; }
        public List<uint> Offsets { get; set; } = new List<uint>();
        public List<WpkFileEntry> Entries { get; set; } = new List<WpkFileEntry>();
    }

    public static class WpkExtractor
    {
        public static WemInformation? ParseWpkFile(string wpkPath, List<StringHash> stringHashes, HashSet<uint>? bnkEventIds = null)
        {
            Defs.LogAction($"Parsing WPK file: {wpkPath}");
            Defs.LogAction($"Looking for {stringHashes.Count} string hashes in WPK file");
            
            if (!File.Exists(wpkPath))
            {
                Defs.EPrintf($"Error: Failed to open \"{wpkPath}\".\n");
                return null;
            }
            var wpkfile = new WpkFile
            {
                Magic = new byte[4],
                Version = 0,
                FileCount = 0,
                OffsetAmount = 0,
                Offsets = new List<uint>(),
                Entries = new List<WpkFileEntry>()
            };
            using (var fs = new FileStream(wpkPath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                // Parse header
                wpkfile.Magic = reader.ReadBytes(4);
                if (Encoding.ASCII.GetString(wpkfile.Magic) != "r3d2")
                    throw new InvalidDataException("Not a WPK file.");
                wpkfile.Version = reader.ReadUInt32();
                wpkfile.FileCount = reader.ReadUInt32();
                ScenarioLogger.LogInfo($"[WPK Header] version={wpkfile.Version}, declaredCount={wpkfile.FileCount}");

                // Parse offsets
                for (int i = 0; i < wpkfile.FileCount; i++)
                    wpkfile.Offsets.Add(reader.ReadUInt32());
                wpkfile.OffsetAmount = wpkfile.FileCount;
                int firstNonZero = -1;
                for (int oi = 0; oi < wpkfile.Offsets.Count; oi++)
                {
                    if (wpkfile.Offsets[oi] != 0) { firstNonZero = oi; break; }
                }
                ScenarioLogger.LogInfo($"[WPK Offsets] totalSlots={wpkfile.OffsetAmount}, firstNonZeroIndex={firstNonZero}");

                // Parse data
                int realFileCount = 0;
                foreach (var offset in wpkfile.Offsets)
                    if (offset != 0) realFileCount++;
                wpkfile.FileCount = (uint)realFileCount;

                int realIndex = 0;
                for (int i = 0; i < wpkfile.OffsetAmount; i++, realIndex++)
                {
                    if (wpkfile.Offsets[i] == 0)
                    {
                        realIndex--;
                        continue;
                    }
                    try
                    {
                        long fileLen = reader.BaseStream.Length;
                        reader.BaseStream.Seek(wpkfile.Offsets[i], SeekOrigin.Begin);
                        long headerPos = reader.BaseStream.Position;
                        ScenarioLogger.LogInfo($"[WPK Entry Begin] slot={i}, offset={wpkfile.Offsets[i]}, headerPos={headerPos}, fileLen={fileLen}");
                        var entry = new WpkFileEntry
                        {
                            DataOffset = reader.ReadUInt32(),
                            DataLength = reader.ReadUInt32(),
                            Filename = string.Empty,
                            Data = new byte[0]
                        };
                        int filenameSize = reader.ReadInt32();
                        if (filenameSize < 0)
                        {
                            ScenarioLogger.LogError($"[WPK Entry] slot={i} invalid nameSize={filenameSize} at headerPos={headerPos}");
                            throw new InvalidDataException("Invalid WPK header (negative nameSize)");
                        }
                        long nameEndCandidate = headerPos + 12 + (filenameSize * 2L);
                        if (nameEndCandidate > fileLen)
                        {
                            ScenarioLogger.LogError($"[WPK Entry] slot={i} nameEnd({nameEndCandidate}) > fileLen({fileLen}), nameSize={filenameSize}, headerPos={headerPos}");
                            throw new EndOfStreamException("WPK header name exceeds file length");
                        }
                        var filenameBytes = new List<byte>(filenameSize);
                        for (int j = 0; j < filenameSize; j++)
                        {
                            filenameBytes.Add(reader.ReadByte());
                            reader.BaseStream.Seek(1, SeekOrigin.Current);
                        }
                        entry.Filename = Encoding.ASCII.GetString(filenameBytes.ToArray());

                        long afterNamePos = reader.BaseStream.Position;
                        ScenarioLogger.LogInfo($"[WPK Entry] slot={i}, headerPos={headerPos}, dataOffset={entry.DataOffset}, dataLength={entry.DataLength}, nameSize={filenameSize}, afterNamePos={afterNamePos}, fileLen={fileLen}");

                        if (entry.DataOffset < afterNamePos)
                        {
                            ScenarioLogger.LogWarning($"[WPK Entry] slot={i} dataOffset({entry.DataOffset}) < afterNamePos({afterNamePos})");
                        }
                        long dataEnd = (long)entry.DataOffset + entry.DataLength;
                        if (dataEnd > fileLen)
                        {
                            ScenarioLogger.LogWarning($"[WPK Entry] slot={i} dataEnd({dataEnd}) exceeds fileLen({fileLen})");
                        }

                        entry.Data = new byte[entry.DataLength];
                        reader.BaseStream.Seek(entry.DataOffset, SeekOrigin.Begin);
                        int bytesRead = reader.Read(entry.Data, 0, (int)entry.DataLength);
                        if (bytesRead != entry.DataLength)
                        {
                            ScenarioLogger.LogError($"[WPK Entry] slot={i} data read mismatch: expected={entry.DataLength}, read={bytesRead}, fileLen={fileLen}");
                            throw new InvalidDataException("Failed to read WEM data.");
                        }
                        wpkfile.Entries.Add(entry);
                        reader.BaseStream.Seek(afterNamePos, SeekOrigin.Begin);
                    }
                    catch (EndOfStreamException eos)
                    {
                        ScenarioLogger.LogError($"[WPK Entry] slot={i} EndOfStream: {eos.Message}; offset={wpkfile.Offsets[i]}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ScenarioLogger.LogError($"[WPK Entry] slot={i} exception: {ex.Message}");
                        throw;
                    }
                }
            }

            var wemInformation = new WemInformation
            {
                SortedWemDataList = new List<AudioData>(),
                OriginalSlotCount = (int)wpkfile.OffsetAmount,  // Preserve original slot count
                WemIdToSlotIndex = new Dictionary<uint, int>(),
                OriginalOffsets = new List<uint>(wpkfile.Offsets),
                SlotIndexToEntry = new Dictionary<int, WpkFileEntry>()
            };

            Defs.LogAction($"WPK contains {wpkfile.Entries.Count} total files in {wpkfile.OffsetAmount} slots");

            // Build slot index mapping - track which slot each WEM was in
            int entryIndex = 0;
            for (int slotIndex = 0; slotIndex < wpkfile.OffsetAmount; slotIndex++)
            {
                if (wpkfile.Offsets[slotIndex] != 0)
                {
                    var entry = wpkfile.Entries[entryIndex];
                    wemInformation.SlotIndexToEntry[slotIndex] = entry;
                    string filenameWithoutExt = entry.Filename;
                    if (filenameWithoutExt.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
                    {
                        filenameWithoutExt = filenameWithoutExt.Substring(0, filenameWithoutExt.Length - 4);
                    }
                    if (uint.TryParse(filenameWithoutExt, out uint wemId))
                    {
                        wemInformation.WemIdToSlotIndex[wemId] = slotIndex;
                        Defs.LogAction($"WPK slot mapping: WEM {wemId} -> slot {slotIndex}");
                    }
                    entryIndex++;
                }
                else
                {
                    Defs.LogAction($"WPK slot {slotIndex} is empty (zero offset)");
                }
            }

            foreach (var entry in wpkfile.Entries)
            {
                Defs.LogAction($"WPK entry: filename='{entry.Filename}', size={entry.DataLength}");
                
                // Strip .wem extension if present
                string filenameWithoutExt = entry.Filename;
                if (filenameWithoutExt.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
                {
                    filenameWithoutExt = filenameWithoutExt.Substring(0, filenameWithoutExt.Length - 4);
                }
                
                if (uint.TryParse(filenameWithoutExt, out uint wemId))
                {
                    wemInformation.SortedWemDataList.Add(new AudioData
                    {
                        Id = wemId,
                        Length = entry.DataLength,
                        Data = entry.Data
                    });
                    Defs.LogAction($"  -> Added as WEM file with ID: {wemId}");
                }
                else
                {
                    Defs.LogAction($"  -> Skipped (not a numeric filename)");
                }
            }
            wemInformation.SortedWemDataList.Sort((a, b) => a.Id.CompareTo(b.Id));
            
            Defs.LogAction($"Found {wemInformation.SortedWemDataList.Count} audio files in WPK");
            
            // Log the first 10 file IDs found in WPK
            for (int i = 0; i < Math.Min(10, wemInformation.SortedWemDataList.Count); i++)
            {
                Defs.LogAction($"WPK file[{i}]: ID={wemInformation.SortedWemDataList[i].Id}, Size={wemInformation.SortedWemDataList[i].Length}");
            }
            
            wemInformation.GroupedWems = GroupWems(wemInformation.SortedWemDataList, stringHashes);
            return wemInformation;
        }

        public static List<StringWithChildren> GroupWems(List<AudioData> wemDataList, List<StringHash> stringHashes)
        {
            Defs.LogAction($"GroupWems: Looking for {stringHashes.Count} string hashes in {wemDataList.Count} WPK files");
            var result = new List<StringWithChildren>();
            var eventMap = new Dictionary<string, StringWithChildren>();
            int matchesFound = 0;
            for (int i = 0; i < stringHashes.Count; i++)
            {
                var currentEvent = stringHashes[i];
                for (int j = 0; j < wemDataList.Count; j++)
                {
                    if (currentEvent.Hash == wemDataList[j].Id)
                    {
                        var currentWem = wemDataList[j];
                        Defs.LogAction($"MATCH FOUND: Hash {currentEvent.Hash} (string: {currentEvent.String}) matches WPK file ID {currentWem.Id}");
                        matchesFound++;
                        if (!eventMap.TryGetValue(currentEvent.String, out var group))
                        {
                            group = new StringWithChildren { String = currentEvent.String };
                            eventMap[currentEvent.String] = group;
                            result.Add(group);
                        }
                        group.Children.Add(currentWem);
                    }
                }
            }
            Defs.LogAction($"GroupWems: Found {matchesFound} matches out of {stringHashes.Count} string hashes");
            return result;
        }
    }
} 
