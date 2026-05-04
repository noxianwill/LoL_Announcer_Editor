using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using LoL_Announcer_Editor.Models;
using LoL_Announcer_Editor.Utils;

namespace LoL_Announcer_Editor.Core
{
    /// <summary>
    /// Service for extracting and parsing Wwise WPK (Riot Package) files.
    /// Handles slot mapping and WEM data retrieval.
    /// </summary>
    public static class WpkExtractor
    {
        /// <summary>
        /// Parses a WPK file and maps its content to provided string hashes and BNK event IDs.
        /// </summary>
        /// <param name="wpkPath">Path to the .wpk file.</param>
        /// <param name="stringHashes">List of hashes from the BIN file.</param>
        /// <param name="bnkEventIds">Optional set of event IDs from the BNK file for filtering.</param>
        /// <returns>A WemInformation object containing the parsed and grouped data.</returns>
        public static WemInformation? ParseWpkFile(string wpkPath, List<StringHash> stringHashes, HashSet<uint>? bnkEventIds = null)
        {
            Logger.LogWpkSection(wpkPath);
            if (!File.Exists(wpkPath))
            {
                Logger.LogWpkError(wpkPath);
                return null;
            }
            try
            {
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

                // Parse offsets
                for (int i = 0; i < wpkfile.FileCount; i++)
                    wpkfile.Offsets.Add(reader.ReadUInt32());
                wpkfile.OffsetAmount = wpkfile.FileCount;

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
                            throw new InvalidDataException("Invalid WPK header (negative nameSize)");
                        }
                        long nameEndCandidate = headerPos + 12 + (filenameSize * 2L);
                        if (nameEndCandidate > fileLen)
                        {
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

                        entry.Data = new byte[entry.DataLength];
                        reader.BaseStream.Seek(entry.DataOffset, SeekOrigin.Begin);
                        int bytesRead = reader.Read(entry.Data, 0, (int)entry.DataLength);
                        if (bytesRead != entry.DataLength)
                        {
                            throw new InvalidDataException("Failed to read WEM data.");
                        }
                        wpkfile.Entries.Add(entry);
                        reader.BaseStream.Seek(afterNamePos, SeekOrigin.Begin);
                    }
                    catch (EndOfStreamException ex)
                    {
                        Logger.LogException("ParseWpkFile - EndOfStreamException", ex);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException("ParseWpkFile - Exception reading WEM data", ex);
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
                    }
                    entryIndex++;
                }
            }

            foreach (var entry in wpkfile.Entries)
            {
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
                }
            }
            wemInformation.SortedWemDataList.Sort((a, b) => a.Id.CompareTo(b.Id));

            Logger.LogWpkParsed(wemInformation.SortedWemDataList.Count);

                wemInformation.GroupedWems = GroupWems(wemInformation.SortedWemDataList, stringHashes);
                return wemInformation;
            }
            catch (Exception ex)
            {
                Logger.LogWpkParseException(wpkPath, ex);
                return null;
            }
        }

        public static List<StringWithChildren> GroupWems(List<AudioData> wemDataList, List<StringHash> stringHashes)
        {
            var result = new List<StringWithChildren>();
            var eventMap = new Dictionary<string, StringWithChildren>();
            for (int i = 0; i < stringHashes.Count; i++)
            {
                var currentEvent = stringHashes[i];
                for (int j = 0; j < wemDataList.Count; j++)
                {
                    if (currentEvent.Hash == wemDataList[j].Id)
                    {
                        var currentWem = wemDataList[j];
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
            return result;
        }
    }
}
