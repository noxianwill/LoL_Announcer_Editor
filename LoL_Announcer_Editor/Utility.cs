using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace LoL_Announcer_Editor
{
    public class ReadableBinaryData
    {
        public byte[] Data { get; set; }
        public ulong Position { get; set; }
        public ulong Size { get; set; }

        public ReadableBinaryData(byte[] data)
        {
            Data = data;
            Position = 0;
            Size = (ulong)data.Length;
        }
    }

    public static class Utility
    {
        // Helper for clamping to next higher multiple of a power of two
        public static int ClampInt(int number, int clamp)
        {
            return ((clamp - 1) + number) & ~(clamp - 1);
        }

        // Helper for distance to next higher multiple of a power of two
        public static int DiffToClamp(int number, int clamp)
        {
            return (clamp - 1) - ((number + clamp - 1) & (clamp - 1));
        }

        // Helper for distance to next lower multiple of a power of two
        public static int DiffFromClamp(int number, int clamp)
        {
            return (number + clamp - 1) & (clamp - 1);
        }

        // Write WPK file preserving original slot structure
        // This is critical for maintaining game compatibility - deleted files become zero slots
        public static void WriteWpkFilePreservingSlots(
            Dictionary<uint, AudioData> wemDataById,  // WEM ID -> AudioData (active files only)
            int originalSlotCount,                      // Original number of slots
            Dictionary<uint, int> wemIdToSlotIndex,     // WEM ID -> original slot index
            HashSet<uint> deletedWemIds,                // IDs of deleted files
            List<AudioData> newWemFiles,                // New files to append
            string outputPath,
            List<uint>? originalOffsets = null,         // Optional: exact original entry offsets per slot
            Dictionary<int, WpkFileEntry>? slotIndexToEntry = null) // Optional: original entry headers per slot
        {
            int totalSlots = originalSlotCount + newWemFiles.Count;
            int[] entryOffsets = new int[totalSlots];
            for (int i = 0; i < originalSlotCount; i++)
            {
                entryOffsets[i] = 0;
            }

            try
            {
                WemAdditionLogger.LogSection("WRITING WPK FILE (SLOT PRESERVATION MODE)");
                WemAdditionLogger.Log($"Output Path: {outputPath}");
                WemAdditionLogger.Log($"Original Slot Count: {originalSlotCount}, New WEMs: {newWemFiles.Count}");

                const int clamp = 8;

                using (var wpkFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(wpkFile))
                {
                    // Write header: "r3d2" magic, version 1, slot count
                    writer.Write(Encoding.ASCII.GetBytes("r3d2\x01\x00\x00\x00"));
                    writer.Write(totalSlots);

                // Reserve space for offset table
                long offsetTableStart = wpkFile.Position;
                wpkFile.Position += totalSlots * 4;
                wpkFile.Position += DiffToClamp((int)wpkFile.Position, clamp);

                int maxWrittenPos = (int)wpkFile.Position;
                int maxDataEnd = (int)wpkFile.Position;

                if (originalOffsets != null && slotIndexToEntry != null)
                {
                    for (int slotIndex = 0; slotIndex < originalSlotCount; slotIndex++)
                    {
                        uint wemIdForSlot = 0;
                        foreach (var kvp in wemIdToSlotIndex)
                        {
                            if (kvp.Value == slotIndex)
                            {
                                wemIdForSlot = kvp.Key;
                                break;
                            }
                        }
                        if (wemIdForSlot == 0 || deletedWemIds.Contains(wemIdForSlot))
                        {
                            entryOffsets[slotIndex] = 0;
                            continue;
                        }
                        if (!slotIndexToEntry.TryGetValue(slotIndex, out var entry))
                        {
                            entryOffsets[slotIndex] = 0;
                            continue;
                        }
                        if (!wemDataById.TryGetValue(wemIdForSlot, out var audioData))
                        {
                            entryOffsets[slotIndex] = 0;
                            continue;
                        }

                        if (slotIndex >= originalOffsets.Count || originalOffsets[slotIndex] == 0)
                        {
                            entryOffsets[slotIndex] = 0;
                            continue;
                        }

                        int entryOffset = (int)originalOffsets[slotIndex];
                        entryOffsets[slotIndex] = entryOffset;
                        wpkFile.Position = entryOffset;
                        int headerPos = entryOffset;
                        writer.Write(entry.DataOffset);
                        writer.Write((uint)audioData.Data.Length);
                        int nameSize = entry.Filename.Length;
                        writer.Write(nameSize);
                        for (int c = 0; c < nameSize; c++)
                        {
                            writer.Write((byte)entry.Filename[c]);
                            writer.Write((byte)0);
                        }
                        wpkFile.Position += DiffToClamp((int)wpkFile.Position, clamp);
                        int afterNamePos = (int)wpkFile.Position;

                        wpkFile.Position = entry.DataOffset;
                        writer.Write(audioData.Data);
                        int dataEndPos = (int)wpkFile.Position;

                        maxWrittenPos = Math.Max(maxWrittenPos, afterNamePos);
                        maxDataEnd = Math.Max(maxDataEnd, unchecked((int)(entry.DataOffset + audioData.Data.Length)));
                    }
                }
                else
                {
                    // Fallback to sequential write if original layout is not available
                    var slots = new AudioData?[totalSlots];
                    foreach (var kvp in wemIdToSlotIndex)
                    {
                        uint wemId = kvp.Key;
                        int slotIndex = kvp.Value;
                        if (deletedWemIds.Contains(wemId))
                        {
                            slots[slotIndex] = null;
                        }
                        else if (wemDataById.TryGetValue(wemId, out var audioData))
                        {
                            slots[slotIndex] = audioData;
                        }
                    }
                    for (int i = 0; i < originalSlotCount; i++)
                    {
                        if (slots[i] == null)
                        {
                            entryOffsets[i] = 0;
                            continue;
                        }
                        entryOffsets[i] = (int)wpkFile.Position;
                        var wem = slots[i]!;
                        wpkFile.Position += 4;
                        writer.Write((uint)wem.Data.Length);
                        string filenameString = $"{wem.Id}.wem";
                        writer.Write(filenameString.Length);
                        for (int c = 0; c < filenameString.Length; c++)
                        {
                            writer.Write((byte)filenameString[c]);
                            writer.Write((byte)0);
                        }
                        wpkFile.Position += DiffToClamp((int)wpkFile.Position, clamp);
                        int currentDataOffset = (int)wpkFile.Position;
                        wpkFile.Position = entryOffsets[i];
                        writer.Write(currentDataOffset);
                        wpkFile.Position = currentDataOffset;
                        writer.Write(wem.Data);
                        maxDataEnd = Math.Max(maxDataEnd, ClampInt(currentDataOffset + wem.Data.Length, clamp));
                    }
                }

                int nextOffset = ClampInt(Math.Max(maxWrittenPos, maxDataEnd), clamp);

                // Repair missing headers for active slots by appending new headers
                if (originalOffsets != null && slotIndexToEntry != null)
                {
                    for (int slotIndex = 0; slotIndex < originalSlotCount; slotIndex++)
                    {
                        if (entryOffsets[slotIndex] != 0) continue;

                        uint wemIdForSlot = 0;
                        foreach (var kvp in wemIdToSlotIndex)
                        {
                            if (kvp.Value == slotIndex) { wemIdForSlot = kvp.Key; break; }
                        }
                        if (wemIdForSlot == 0 || deletedWemIds.Contains(wemIdForSlot)) continue;
                        if (!wemDataById.TryGetValue(wemIdForSlot, out var audioData)) continue;

                        int entryOffset = nextOffset;
                        entryOffsets[slotIndex] = entryOffset;

                        wpkFile.Position = entryOffset;
                        writer.Write(0);
                        writer.Write((uint)audioData.Data.Length);
                        string filenameString = $"{wemIdForSlot}.wem";
                        writer.Write(filenameString.Length);
                        for (int c = 0; c < filenameString.Length; c++)
                        {
                            writer.Write((byte)filenameString[c]);
                            writer.Write((byte)0);
                        }
                        wpkFile.Position += DiffToClamp((int)wpkFile.Position, clamp);
                        int afterNamePosFix = (int)wpkFile.Position;

                        int dataOffsetFix = afterNamePosFix;
                        wpkFile.Position = entryOffset;
                        writer.Write(dataOffsetFix);
                        wpkFile.Position = dataOffsetFix;
                        writer.Write(audioData.Data);
                        int dataEndPosFix = (int)wpkFile.Position;

                        nextOffset = ClampInt(dataOffsetFix + audioData.Data.Length, clamp);
                        maxWrittenPos = Math.Max(maxWrittenPos, afterNamePosFix);
                        maxDataEnd = Math.Max(maxDataEnd, dataEndPosFix);
                    }
                }
                WemAdditionLogger.LogSection("WriteWpkFilePreservingSlots - New WEM Files");
                WemAdditionLogger.Log($"Adding {newWemFiles.Count} new WEM files starting at slot {originalSlotCount}");
                for (int i = 0; i < newWemFiles.Count; i++)
                {
                    int slotIndex = originalSlotCount + i;
                    var wem = newWemFiles[i];
                    int entryOffset = nextOffset;
                    entryOffsets[slotIndex] = entryOffset;

                    WemAdditionLogger.Log($"  Writing new WEM: Id={wem.Id}, SlotIndex={slotIndex}, EntryOffset={entryOffset}, DataLength={wem.Data.Length}");

                    wpkFile.Position = entryOffset;
                    writer.Write(0);
                    writer.Write((uint)wem.Data.Length);
                    string filenameString = $"{wem.Id}.wem";
                    writer.Write(filenameString.Length);
                    for (int c = 0; c < filenameString.Length; c++)
                    {
                        writer.Write((byte)filenameString[c]);
                        writer.Write((byte)0);
                    }
                    wpkFile.Position += DiffToClamp((int)wpkFile.Position, clamp);
                    int afterNamePosNew = (int)wpkFile.Position;

                    int dataOffset = afterNamePosNew;
                    wpkFile.Position = entryOffset;
                    writer.Write(dataOffset);
                    wpkFile.Position = dataOffset;
                    writer.Write(wem.Data);
                    int dataEndPosNew = (int)wpkFile.Position;

                    WemAdditionLogger.Log($"  WEM {wem.Id}: DataOffset={dataOffset}, DataEndPos={dataEndPosNew}");
                    nextOffset = ClampInt(dataOffset + wem.Data.Length, clamp);
                }

                // Write offset table
                for (int i = 0; i < totalSlots; i++)
                {
                    wpkFile.Position = offsetTableStart + i * 4;
                    writer.Write(entryOffsets[i]);
                }

                WemAdditionLogger.LogSuccess($"WPK file written successfully to {outputPath}. Total size: {new FileInfo(outputPath).Length} bytes.");
                }
            }
            catch (Exception ex)
            {
                WemAdditionLogger.LogException("WriteWpkFilePreservingSlots", ex);
                throw;
            }
            VerifyWpkIntegrity(outputPath, totalSlots, entryOffsets);
        }

        // Write WPK file from a list of AudioData (legacy method - does not preserve slots)
        public static void WriteWpkFile(List<AudioData> wemFiles, string outputPath)
        {
            const int clamp = 8;

            using (var wpkFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(wpkFile))
            {
                // Write header
                writer.Write(Encoding.ASCII.GetBytes("r3d2\x01\x00\x00\x00"));
                writer.Write(wemFiles.Count);

                // Skip over the initial offset section and write them later
                long offsetSectionStart = wpkFile.Position;
                wpkFile.Position += wemFiles.Count * 4;
                wpkFile.Position += DiffToClamp((int)wpkFile.Position, clamp);

                var offsetList = new List<int>(wemFiles.Count);
                for (int i = 0; i < wemFiles.Count; i++)
                {
                    // Save offset to fill in the offset section later
                    offsetList.Add((int)wpkFile.Position);
                    wpkFile.Position += 4;
                    writer.Write(wemFiles[i].Length);
                    string filenameString = $"{wemFiles[i].Id}.wem";
                    writer.Write(filenameString.Length);
                    foreach (char c in filenameString)
                    {
                        writer.Write((byte)c);
                        wpkFile.Position += 1;
                    }
                    wpkFile.Position += DiffToClamp((int)wpkFile.Position, clamp);
                }

                int startDataOffset = (int)wpkFile.Position;

                for (int i = 0; i < wemFiles.Count; i++)
                {
                    // Seek to initial place in the offset section and write offset
                    wpkFile.Position = 12 + 4 * i;
                    writer.Write(offsetList[i]);

                    // Seek to the written offset, update the offset variable for further use and write data offset
                    wpkFile.Position = offsetList[i];
                    // CRITICAL: Use Data.Length, not Length field, because Length might be stale from original parse
                    offsetList[i] = i == 0 ? startDataOffset : ClampInt(offsetList[i - 1] + wemFiles[i - 1].Data.Length, clamp);
                    writer.Write(offsetList[i]);

                    // Seek to written data offset and write data
                    wpkFile.Position = offsetList[i];
                    writer.Write(wemFiles[i].Data);
                }
            }
        }

        // Append WEM files to existing WPK file
        public static bool AppendWemToWpk(List<AudioData> newWemFiles, string wpkPath)
        {
            try
            {
                if (!File.Exists(wpkPath))
                {
                    return false;
                }

                // Use existing working WPK parsing logic
                var existingWemInfo = WpkExtractor.ParseWpkFile(wpkPath, new List<StringHash>(), null);
                if (existingWemInfo == null)
                {
                    return false;
                }

                // Combine existing and new WEM files
                var allWemFiles = new List<AudioData>(existingWemInfo.SortedWemDataList);
                allWemFiles.AddRange(newWemFiles);

                // Write combined WPK file
                WriteWpkFile(allWemFiles, wpkPath);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Write BNK file from a list of AudioData
        public static void WriteBnkFile(List<AudioData> wemFiles, string outputPath)
        {
            const int clamp = 16;
            const uint version = 0x86; // TODO: Should be taken from the original source file
            const uint bkhdSectionLength = 0x14; // TODO: Should be taken from the original source file
            using (var bnkFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(bnkFile))
            {
                // Write BKHD section
                writer.Write(Encoding.ASCII.GetBytes("BKHD"));
                writer.Write(bkhdSectionLength);
                writer.Write(version);
                writer.Write(0u); // TODO: id of this bnk file
                writer.Write(0x17705d3e); // random hardcoded bytes?
                writer.Write(new byte[bkhdSectionLength - 12]);

                // Write DIDX section
                writer.Write(Encoding.ASCII.GetBytes("DIDX"));
                writer.Write((uint)(wemFiles.Count * 12));
                int initialClampOffset = (int)bnkFile.Position + wemFiles.Count * 12 + 8;
                var offsetList = new List<int>(wemFiles.Count);
                for (int i = 0; i < wemFiles.Count; i++)
                {
                    int offset = i == 0 ? 0 : offsetList[i - 1] + (int)wemFiles[i - 1].Length;
                    offset += DiffToClamp(offset + initialClampOffset, clamp);
                    offsetList.Add(offset);
                    writer.Write(wemFiles[i].Id);
                    writer.Write(offset);
                    writer.Write(wemFiles[i].Length);
                }

                // Write DATA section
                writer.Write(Encoding.ASCII.GetBytes("DATA"));
                writer.Write((uint)(offsetList[wemFiles.Count - 1] + (int)wemFiles[wemFiles.Count - 1].Length));
                for (int i = 0; i < wemFiles.Count; i++)
                {
                    bnkFile.Position += DiffToClamp((int)bnkFile.Position, clamp);
                    writer.Write(wemFiles[i].Data);
                }
            }
        }
        private static void VerifyWpkIntegrity(string path, int slotCount, int[] offsets)
        {
            WemAdditionLogger.LogSection("WPK BIT-LEVEL INTEGRITY CHECK");
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    // Check magic
                    byte[] magic = reader.ReadBytes(4);
                    if (Encoding.ASCII.GetString(magic) != "r3d2")
                        WemAdditionLogger.Log("!!! WARNING: WPK Magic 'r3d2' missing!");
                    
                    // Check slot count
                    fs.Position = 8;
                    int actualSlots = reader.ReadInt32();
                    WemAdditionLogger.Log($"Verified Slot Count: {actualSlots} (Expected: {slotCount})");

                    // Check individual offsets and WEM signatures
                    int validWems = 0;
                    for (int i = 0; i < slotCount; i++)
                    {
                        if (offsets[i] != 0)
                        {
                            fs.Position = offsets[i];
                            int dataOffset = reader.ReadInt32();
                            if (dataOffset == 0)
                            {
                                WemAdditionLogger.Log($"!!! CRITICAL: Slot {i} has header at {offsets[i]} but data offset is ZERO!");
                                continue;
                            }
                            
                            fs.Position = dataOffset;
                            byte[] sig = reader.ReadBytes(4);
                            
                            // WEM files start with RIFF or WAVE or have a specific header
                            // We just want to make sure it's not zeros
                            if (sig[0] == 0 && sig[1] == 0 && sig[2] == 0 && sig[3] == 0)
                            {
                                WemAdditionLogger.Log($"!!! CRITICAL: Slot {i} at data offset {dataOffset} contains ZEROS!");
                            }
                            else
                            {
                                validWems++;
                            }
                        }
                    }
                    WemAdditionLogger.Log($"Verified {validWems} active WEM slots with non-zero data.");
                }
                WemAdditionLogger.Log("WPK BIT-LEVEL CHECK COMPLETE");
            }
            catch (Exception ex)
            {
                WemAdditionLogger.Log($"!!! WPK INTEGRITY ERROR: {ex.Message}");
            }
        }
    }
}
