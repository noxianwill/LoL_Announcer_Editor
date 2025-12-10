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
            const int clamp = 8;
            BnkWpkLogger.LogOperationStart("Write WPK File (Preserving Slots)");
            BnkWpkLogger.LogInfo($"WriteWpkFilePreservingSlots: Starting with {originalSlotCount} slots, {deletedWemIds.Count} deleted, {newWemFiles.Count} new");

            int totalSlots = originalSlotCount + newWemFiles.Count;

            // Prepare entry offsets table
            int[] entryOffsets = new int[totalSlots];
            for (int i = 0; i < originalSlotCount; i++)
            {
                entryOffsets[i] = 0;
            }

            using (var wpkFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(wpkFile))
            {
                // Write header: "r3d2" magic, version 1, slot count
                writer.Write(Encoding.ASCII.GetBytes("r3d2\x01\x00\x00\x00"));
                writer.Write(totalSlots);
                BnkWpkLogger.LogInfo($"WriteWpkFilePreservingSlots: Wrote header with {totalSlots} slots");

                // Reserve space for offset table
                long offsetTableStart = wpkFile.Position;
                wpkFile.Position += totalSlots * 4;
                wpkFile.Position += DiffToClamp((int)wpkFile.Position, clamp);
                BnkWpkLogger.LogInfo($"WriteWpkFilePreservingSlots: Reserved offset table at {offsetTableStart}, length={totalSlots * 4}");

                int maxWrittenPos = (int)wpkFile.Position;
                int maxDataEnd = (int)wpkFile.Position;

                if (originalOffsets != null && slotIndexToEntry != null)
                {
                    BnkWpkLogger.LogInfo($"WriteWpkFilePreservingSlots: Using preserved layout: originalOffsets={originalOffsets.Count}, slotHeaders={slotIndexToEntry.Count}, wemIdMap={wemIdToSlotIndex.Count}");
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
                            if (wemIdForSlot == 0)
                            {
                                BnkWpkLogger.LogWarning($"WriteWpkFilePreservingSlots: Slot {slotIndex}: no mapped WEM id, originalOffset={(originalOffsets.Count > slotIndex ? originalOffsets[slotIndex] : 0)}");
                                ScenarioLogger.LogWarning($"Slot {slotIndex}: no mapped WEM id");
                            }
                            else
                            {
                                BnkWpkLogger.LogInfo($"WriteWpkFilePreservingSlots: Slot {slotIndex}: WEM {wemIdForSlot} marked deleted -> zero offset");
                                ScenarioLogger.LogInfo($"Slot {slotIndex}: deleted WEM {wemIdForSlot}");
                            }
                            continue;
                        }
                        if (!slotIndexToEntry.TryGetValue(slotIndex, out var entry))
                        {
                            entryOffsets[slotIndex] = 0;
                            BnkWpkLogger.LogWarning($"WriteWpkFilePreservingSlots: Slot {slotIndex}: missing original entry header -> zero offset");
                            ScenarioLogger.LogWarning($"Slot {slotIndex}: missing original header");
                            continue;
                        }
                        if (!wemDataById.TryGetValue(wemIdForSlot, out var audioData))
                        {
                            entryOffsets[slotIndex] = 0;
                            BnkWpkLogger.LogWarning($"WriteWpkFilePreservingSlots: Slot {slotIndex}: missing audio data for WEM {wemIdForSlot} -> zero offset");
                            ScenarioLogger.LogWarning($"Slot {slotIndex}: missing audio for WEM {wemIdForSlot}");
                            continue;
                        }

                        if (slotIndex >= originalOffsets.Count || originalOffsets[slotIndex] == 0)
                        {
                            entryOffsets[slotIndex] = 0;
                            BnkWpkLogger.LogWarning($"WriteWpkFilePreservingSlots: Slot {slotIndex}: no original offset available -> zero offset");
                            ScenarioLogger.LogWarning($"Slot {slotIndex}: no original offset");
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
                        BnkWpkLogger.LogInfo($"WriteWpkFilePreservingSlots: Slot {slotIndex}: PRESERVED WEM {wemIdForSlot}, headerPos={headerPos}, afterNamePos={afterNamePos}, dataOffset={entry.DataOffset}, dataEnd={dataEndPos}, size={audioData.Data.Length}");
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
                        BnkWpkLogger.LogInfo($"WriteWpkFilePreservingSlots: Slot {slotIndex}: REPAIRED HEADER for WEM {wemIdForSlot}, headerPos={entryOffset}, afterNamePos={afterNamePosFix}, dataOffset={dataOffsetFix}, dataEnd={dataEndPosFix}, size={audioData.Data.Length}");
                        ScenarioLogger.LogInfo($"Repaired missing header at slot {slotIndex} for WEM {wemIdForSlot}");
                    }
                }
                for (int i = 0; i < newWemFiles.Count; i++)
                {
                    int slotIndex = originalSlotCount + i;
                    var wem = newWemFiles[i];
                    int entryOffset = nextOffset;
                    entryOffsets[slotIndex] = entryOffset;

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

                    nextOffset = ClampInt(dataOffset + wem.Data.Length, clamp);
                    BnkWpkLogger.LogInfo($"WriteWpkFilePreservingSlots: Slot {slotIndex}: NEW WEM {wem.Id}, headerPos={entryOffset}, afterNamePos={afterNamePosNew}, dataOffset={dataOffset}, dataEnd={dataEndPosNew}, size={wem.Data.Length}");
                }

                // Write offset table
                for (int i = 0; i < totalSlots; i++)
                {
                    wpkFile.Position = offsetTableStart + i * 4;
                    writer.Write(entryOffsets[i]);
                }

                int nonZero = 0;
                for (int i = 0; i < totalSlots; i++) if (entryOffsets[i] != 0) nonZero++;
                BnkWpkLogger.LogInfo($"WriteWpkFilePreservingSlots: Offset table written: nonZero={nonZero}/{totalSlots}");
                int previewCount = Math.Min(10, totalSlots);
                var preview = new StringBuilder();
                for (int i = 0; i < previewCount; i++)
                {
                    if (i > 0) preview.Append(", ");
                    preview.Append(entryOffsets[i]);
                }
                BnkWpkLogger.LogInfo($"WriteWpkFilePreservingSlots: First {previewCount} offsets: [{preview}]");
                ScenarioLogger.LogInfo($"Saved WPK offsets nonZero={nonZero}/{totalSlots}");

                BnkWpkLogger.LogInfo($"WriteWpkFilePreservingSlots: Completed, final file size: {wpkFile.Length} bytes");
                BnkWpkLogger.LogOperationEnd("Write WPK File (Preserving Slots)", true);
            }
        }

        // Write WPK file from a list of AudioData (legacy method - does not preserve slots)
        public static void WriteWpkFile(List<AudioData> wemFiles, string outputPath)
        {
            const int clamp = 8;
            BnkWpkLogger.LogOperationStart("Write WPK File");
            BnkWpkLogger.LogInfo($"WriteWpkFile: Starting to write {wemFiles.Count} WEM files to {outputPath}");
            
            using (var wpkFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(wpkFile))
            {
                // Write header
                writer.Write(Encoding.ASCII.GetBytes("r3d2\x01\x00\x00\x00"));
                writer.Write(wemFiles.Count);
                BnkWpkLogger.LogInfo($"WriteWpkFile: Wrote header with {wemFiles.Count} files");

                // Skip over the initial offset section and write them later
                long offsetSectionStart = wpkFile.Position;
                wpkFile.Position += wemFiles.Count * 4;
                wpkFile.Position += DiffToClamp((int)wpkFile.Position, clamp);
                BnkWpkLogger.LogInfo($"WriteWpkFile: Skipped offset section, now at position {wpkFile.Position}");

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
                    
                    BnkWpkLogger.LogInfo($"WriteWpkFile: File {i}: ID={wemFiles[i].Id}, Length={wemFiles[i].Length}, Filename={filenameString}, Offset={offsetList[i]}");
                }

                int startDataOffset = (int)wpkFile.Position;
                BnkWpkLogger.LogInfo($"WriteWpkFile: Data section starts at offset {startDataOffset}");
                
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
                    
                    BnkWpkLogger.LogInfo($"WriteWpkFile: Wrote data for file {i}: ID={wemFiles[i].Id}, DataLength={wemFiles[i].Data.Length}, Position={wpkFile.Position}");
                }
                
                BnkWpkLogger.LogInfo($"WriteWpkFile: Completed writing {wemFiles.Count} files, final file size: {wpkFile.Length} bytes");
                BnkWpkLogger.LogOperationEnd("Write WPK File", true);
            }
        }

        // Append WEM files to existing WPK file
        public static bool AppendWemToWpk(List<AudioData> newWemFiles, string wpkPath)
        {
            BnkWpkLogger.LogOperationStart("Append WEM to WPK");
            try
            {
                BnkWpkLogger.LogInfo($"AppendWemToWpk: Starting to append {newWemFiles.Count} WEM files to {wpkPath}");
                
                if (!File.Exists(wpkPath))
                {
                    BnkWpkLogger.LogError($"AppendWemToWpk: WPK file not found: {wpkPath}");
                    BnkWpkLogger.LogOperationEnd("Append WEM to WPK", false);
                    return false;
                }

                // Use existing working WPK parsing logic
                var existingWemInfo = WpkExtractor.ParseWpkFile(wpkPath, new List<StringHash>(), null);
                if (existingWemInfo == null)
                {
                    BnkWpkLogger.LogError("AppendWemToWpk: Failed to parse existing WPK file");
                    BnkWpkLogger.LogOperationEnd("Append WEM to WPK", false);
                    return false;
                }
                
                BnkWpkLogger.LogInfo($"AppendWemToWpk: Existing WPK has {existingWemInfo.SortedWemDataList.Count} files");
                
                // Combine existing and new WEM files
                var allWemFiles = new List<AudioData>(existingWemInfo.SortedWemDataList);
                allWemFiles.AddRange(newWemFiles);
                
                // Write combined WPK file
                WriteWpkFile(allWemFiles, wpkPath);
                
                BnkWpkLogger.LogInfo($"AppendWemToWpk: Successfully appended {newWemFiles.Count} files to WPK");
                BnkWpkLogger.LogOperationEnd("Append WEM to WPK", true);
                return true;
            }
            catch (Exception ex)
            {
                BnkWpkLogger.LogError($"AppendWemToWpk: Failed to append WEM files: {ex.Message}");
                BnkWpkLogger.LogOperationEnd("Append WEM to WPK", false);
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
    }
} 
