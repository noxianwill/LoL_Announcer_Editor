using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace LoL_Announcer_Editor
{
    public static class BnkWriter
    {
        private static HashSet<uint> _affectedFileIds = new HashSet<uint>();

        public static bool WriteBnkFile(BNKSections sections, string outputPath, uint bnkVersion = 0x86)
        {
            BnkLogger.LogOperationStart("Write BNK File");
            try
            {
                BnkLogger.LogInfo($"Writing BNK file to: {outputPath}");
                BnkLogger.LogInfo($"BNK version: {bnkVersion:X}");
                
                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(fs))
                {
                    // Write BKHD section
                    WriteBkhdSection(writer, bnkVersion);
                    
                    // Write HIRC section
                    WriteHircSection(writer, sections, bnkVersion);
                }
                
                BnkLogger.LogInfo($"Successfully wrote BNK file: {outputPath}");
                BnkLogger.LogOperationEnd("Write BNK File", true);
                return true;
            }
            catch (Exception ex)
            {
                BnkLogger.LogError($"Failed to write BNK file: {ex.Message}");
                BnkLogger.LogOperationEnd("Write BNK File", false);
                return false;
            }
        }
        
        private static void WriteBkhdSection(BinaryWriter writer, uint version)
        {
            const uint bkhdSectionLength = 0x14;
            
            // Write BKHD header
            writer.Write(Encoding.ASCII.GetBytes("BKHD"));
            writer.Write(bkhdSectionLength);
            writer.Write(version);
            writer.Write(0u); // BNK ID
            writer.Write(0x17705d3e); // Random hardcoded bytes
            writer.Write(new byte[bkhdSectionLength - 12]); // Padding
            
            BnkLogger.LogInfo("Wrote BKHD section");
        }
        
        private static void WriteHircSection(BinaryWriter writer, BNKSections sections, uint bnkVersion)
        {
            BnkLogger.LogInfo("WriteHircSection: Starting HIRC section calculation");
            // Calculate HIRC section size
            uint hircSize = 4; // for the object count
            foreach (var sound in sections.Sounds)
                hircSize += 1 + 4 + CalculateSoundObjectSize(sound, bnkVersion);
            foreach (var ea in sections.EventActions)
                hircSize += 1 + 4 + CalculateEventActionObjectSize(ea);
            foreach (var evt in sections.Events)
                hircSize += 1 + 4 + CalculateEventObjectSize(evt, bnkVersion);
            foreach (var rc in sections.RandomContainers)
                hircSize += 1 + 4 + CalculateRandomContainerObjectSize(rc, bnkVersion);
            foreach (var sc in sections.SwitchContainers)
                hircSize += 1 + 4 + CalculateSwitchContainerObjectSize(sc, bnkVersion);
            foreach (var ms in sections.MusicSegments)
                hircSize += 1 + 4 + CalculateMusicContainerObjectSize(ms, bnkVersion);
            foreach (var mt in sections.MusicTracks)
                hircSize += 1 + 4 + CalculateMusicTrackObjectSize(mt, bnkVersion);
            foreach (var msi in sections.MusicSwitches)
                hircSize += 1 + 4 + CalculateMusicSwitchObjectSize(msi, bnkVersion);
            foreach (var mpl in sections.MusicPlaylists)
                hircSize += 1 + 4 + CalculateMusicContainerObjectSize(mpl, bnkVersion);

            BnkLogger.LogInfo($"WriteHircSection: Object counts - Sounds: {sections.Sounds.Count}, EventActions: {sections.EventActions.Count}, Events: {sections.Events.Count}, RandomContainers: {sections.RandomContainers.Count}, SwitchContainers: {sections.SwitchContainers.Count}, MusicSegments: {sections.MusicSegments.Count}, MusicTracks: {sections.MusicTracks.Count}, MusicSwitches: {sections.MusicSwitches.Count}, MusicPlaylists: {sections.MusicPlaylists.Count}");

            writer.Write(Encoding.ASCII.GetBytes("HIRC"));
            writer.Write(hircSize);
            uint totalObjects = (uint)(sections.Sounds.Count + sections.EventActions.Count + sections.Events.Count + 
                sections.RandomContainers.Count + sections.SwitchContainers.Count + sections.MusicSegments.Count + 
                sections.MusicTracks.Count + sections.MusicSwitches.Count + sections.MusicPlaylists.Count);
            writer.Write(totalObjects);
            BnkLogger.LogInfo($"Writing HIRC section with {totalObjects} objects");
            int objectIndex = 0;
            // Sample log for first 3 objects of each type
            for (int i = 0; i < sections.Sounds.Count; i++)
            {
                WriteSoundObject(writer, sections.Sounds[i], bnkVersion);
                objectIndex++;
            }
            for (int i = 0; i < sections.EventActions.Count; i++)
            {
                WriteEventActionObject(writer, sections.EventActions[i]);
                objectIndex++;
            }
            for (int i = 0; i < sections.Events.Count; i++)
            {
                WriteEventObject(writer, sections.Events[i], bnkVersion);
                objectIndex++;
            }
            for (int i = 0; i < sections.RandomContainers.Count; i++)
            {
                WriteRandomContainerObject(writer, sections.RandomContainers[i], bnkVersion);
                objectIndex++;
            }
            for (int i = 0; i < sections.SwitchContainers.Count; i++)
            {
                WriteSwitchContainerObject(writer, sections.SwitchContainers[i], bnkVersion);
                objectIndex++;
            }
            for (int i = 0; i < sections.MusicSegments.Count; i++)
            {
                WriteMusicContainerObject(writer, sections.MusicSegments[i], bnkVersion, 10);
                objectIndex++;
            }
            for (int i = 0; i < sections.MusicTracks.Count; i++)
            {
                WriteMusicTrackObject(writer, sections.MusicTracks[i], bnkVersion);
                objectIndex++;
            }
            for (int i = 0; i < sections.MusicSwitches.Count; i++)
            {
                WriteMusicSwitchObject(writer, sections.MusicSwitches[i], bnkVersion);
                objectIndex++;
            }
            for (int i = 0; i < sections.MusicPlaylists.Count; i++)
            {
                WriteMusicContainerObject(writer, sections.MusicPlaylists[i], bnkVersion, 13);
                objectIndex++;
            }
            BnkLogger.LogInfo($"WriteHircSection: Completed writing {objectIndex} objects");
        }
        
        // Helper methods for calculating object sizes
        private static uint CalculateSoundObjectSize(Sound sound, uint bnkVersion)
        {
            uint size = 4 + 4 + 1; // SelfId + Unknown1 + IsStreamed
            if (bnkVersion <= 0x59) size += 3;
            if (bnkVersion <= 0x70) size += 4;
            size += 4; // FileId

            // Add size for extended Sound fields if they exist
            if (sound.SoundStructure != null)
            {
                size += 4; // Reserved0
                size += 4; // StreamSourceType (int, not string!)
                size += 4; // StreamFileID
                size += 4; // StreamSourceID

                // If embedded (StreamSourceType == 0), add offset/length fields
                if (sound.StreamSourceType == 0)
                {
                    size += 4; // StreamFileOffset
                    size += 4; // StreamFileLength
                }

                size += 1; // SoundType (byte, not string!)

                // SoundStructure fields (following Wwise BNK format)
                size += 4; // PluginID
                size += 2; // Type
                size += 2; // Company
                size += 1; // StreamType
                size += 4; // SourceID
                size += 4; // InMemoryMediaSize
                size += 1; // SourceBits (bitfield)
                size += 1; // BIsOverrideParentFX
                size += 1; // NumFx
                size += 1; // BIsOverrideParentMetadata
                size += 1; // NumPluginMetadata
                size += 1; // BOverrideAttachmentParams
                size += 4; // OverrideBusID
                size += 4; // DirectParentID
            }

            return size;
        }
        
        private static uint CalculateEventActionObjectSize(EventAction eventAction)
        {
            uint size = 10; // SelfId (4) + Scope (1) + Type (1) + SoundObjectId (4) + SwitchGroupId (4)
            
            // Handle different EventAction types based on the reading logic
            if (eventAction.Type == 25 || eventAction.Type == 18)
            {
                size += 5; // 5 bytes of padding
                size += 64; // Base params size
                size += 4; // SwitchGroupId (written before SoundObjectId for these types)
                size += 4; // SoundObjectId
            }
            
            return size;
        }
        
        private static uint CalculateEventObjectSize(Event evt, uint bnkVersion)
        {
            return 5 + (uint)(evt.EventIds.Count * 4); // SelfId (4) + EventAmount (1) + EventIds (4 each)
        }
        
        private static int GetBaseParamsSize(uint bnkVersion)
        {
            // For BNK version 0x86, use a safe estimate of 64 bytes (adjust if needed)
            return 64;
        }
        
        private static void WriteBaseParams(BinaryWriter writer, uint bnkVersion)
        {
            // For BNK version 0x86, write 64 bytes of zeros (adjust if needed)
            writer.Write(new byte[64]);
        }
        
        private static uint CalculateRandomContainerObjectSize(RandomContainer obj, uint bnkVersion)
        {
            uint size = 4; // SelfId
            size += (uint)GetBaseParamsSize(bnkVersion); // size of base params
            size += 24; // padding after base params
            size += 4; // SoundIdAmount
            size += (uint)(obj.SoundIds.Count * 4); // SoundIds
            return size;
        }
        
        private static uint CalculateSwitchContainerObjectSize(SwitchContainer switchContainer, uint bnkVersion)
        {
            return 13 + (uint)(switchContainer.Children.Count * 4); // SelfId (4) + ParentId (4) + GroupType (1) + GroupId (4) + NumChildren (4) + Children (4 each)
        }
        
        private static uint CalculateMusicContainerObjectSize(MusicContainer musicContainer, uint bnkVersion)
        {
            return 16 + (uint)(musicContainer.MusicTrackIds.Count * 4); // SelfId (4) + MusicSwitchId (4) + SoundObjectId (4) + MusicTrackIdAmount (4) + MusicTrackIds (4 each)
        }
        
        private static uint CalculateMusicTrackObjectSize(MusicTrack musicTrack, uint bnkVersion)
        {
            uint size = 13 + (uint)(musicTrack.FileIds.Count * 4); // SelfId (4) + TrackCount (4) + FileIds (4 each) + HasSwitchIds (1) + SwitchGroupId (4)
            if (musicTrack.HasSwitchIds)
            {
                size += (uint)(musicTrack.SwitchIds.Count * 4); // SwitchIds (4 each)
            }
            return size;
        }
        
        private static uint CalculateMusicSwitchObjectSize(MusicSwitch musicSwitch, uint bnkVersion)
        {
            uint size = 16 + (uint)(musicSwitch.Children.Count * 4); // SelfId (4) + ParentId (4) + NumChildren (4) + Children (4 each) + NumArguments (4)
            size += (uint)(musicSwitch.Arguments.Count * 5); // Arguments (5 each)
            size += 4 + (uint)(musicSwitch.Nodes.Count * 8); // NumNodes (4) + Nodes (8 each)
            return size;
        }
        
        // Helper methods for writing objects
        private static void WriteSoundObject(BinaryWriter writer, Sound sound, uint bnkVersion)
        {
            if (sound.RawData != null && sound.RawData.Length > 0)
            {
                writer.Write(sound.RawData);
                return;
            }
            writer.Write((byte)2); // type
            uint size = CalculateSoundObjectSize(sound, bnkVersion);
            writer.Write(size);
            writer.Write(sound.SelfId);
            writer.Write(sound.Unknown1);
            writer.Write(sound.IsStreamed);
            if (bnkVersion <= 0x59)
                writer.Write(sound.Unknown2 ?? new byte[3]);
            if (bnkVersion <= 0x70)
                writer.Write(sound.Unknown3 ?? new byte[4]);
            writer.Write(sound.FileId);
            
            // Write extended Sound fields if they exist
            if (sound.SoundStructure != null)
            {
                writer.Write(sound.Reserved0);                         // 4 bytes
                writer.Write(sound.StreamSourceType);                  // 4 bytes (int: 0=Embedded, 1=Streamed, 2=Streamed ZeroLatency)
                writer.Write(sound.StreamFileID);                      // 4 bytes
                writer.Write(sound.StreamSourceID);                    // 4 bytes

                // If embedded (StreamSourceType == 0), write offset/length fields
                if (sound.StreamSourceType == 0)
                {
                    writer.Write(sound.StreamFileOffset);              // 4 bytes
                    writer.Write(sound.StreamFileLength);              // 4 bytes
                }

                writer.Write(sound.SoundType);                         // 1 byte (0=SFX, 1=Voice)

                // Write SoundStructure fields (following Wwise BNK format)
                writer.Write(sound.SoundStructure.PluginID);           // 4 bytes
                writer.Write(sound.SoundStructure.Type);               // 2 bytes
                writer.Write(sound.SoundStructure.Company);            // 2 bytes
                writer.Write(sound.SoundStructure.StreamType);         // 1 byte
                writer.Write(sound.SoundStructure.SourceID);           // 4 bytes
                writer.Write(sound.SoundStructure.InMemoryMediaSize);  // 4 bytes
                writer.Write(sound.SoundStructure.SourceBits);         // 1 byte (bitfield)
                writer.Write(sound.SoundStructure.BIsOverrideParentFX);      // 1 byte
                writer.Write(sound.SoundStructure.NumFx);                     // 1 byte
                writer.Write(sound.SoundStructure.BIsOverrideParentMetadata); // 1 byte
                writer.Write(sound.SoundStructure.NumPluginMetadata);         // 1 byte
                writer.Write(sound.SoundStructure.BOverrideAttachmentParams); // 1 byte
                writer.Write(sound.SoundStructure.OverrideBusID);             // 4 bytes
                writer.Write(sound.SoundStructure.DirectParentID);            // 4 bytes
            }
            
            // Minimal, non-spammy log for affected/new WEMs
            if (_affectedFileIds.Contains(sound.FileId))
                BnkLogger.LogInfo($"[Write] Sound object for new/affected WEM: SelfId={sound.SelfId}, FileId={sound.FileId}, IsStreamed={sound.IsStreamed}");
        }
        
        private static void WriteEventActionObject(BinaryWriter writer, EventAction eventAction)
        {
            if (eventAction.RawData != null && eventAction.RawData.Length > 0)
            {
                writer.Write(eventAction.RawData);
                return;
            }
            long startPos = writer.BaseStream.Position;
            writer.Write((byte)3); // Type 3
            uint length = CalculateEventActionObjectSize(eventAction);
            writer.Write(length);
            writer.Write(eventAction.SelfId);
            writer.Write(eventAction.Scope);
            writer.Write(eventAction.Type);
            
            // Handle different EventAction types based on the reading logic
            if (eventAction.Type == 25 || eventAction.Type == 18)
            {
                // Write 5 bytes of padding
                writer.Write(new byte[5]);
                // Write base params (simplified - just write zeros for now)
                writer.Write(new byte[64]); // Base params size
                writer.Write(eventAction.SwitchGroupId);
                writer.Write(eventAction.SoundObjectId);
            }
            else
            {
                writer.Write(eventAction.SoundObjectId);
                writer.Write(eventAction.SwitchGroupId);
            }
            long endPos = writer.BaseStream.Position;
        }
        
        private static void WriteEventObject(BinaryWriter writer, Event evt, uint bnkVersion)
        {
            if (evt.RawData != null && evt.RawData.Length > 0)
            {
                writer.Write(evt.RawData);
                return;
            }
            // Debug log for diagnosis
            BnkLogger.LogInfo($"[DEBUG] Writing Event: SelfId={evt.SelfId}, EventAmount={evt.EventAmount}, EventIds=[{string.Join(", ", evt.EventIds)}]");
            long startPos = writer.BaseStream.Position;
            writer.Write((byte)4); // Type 4
            uint length = CalculateEventObjectSize(evt, bnkVersion);
            writer.Write(length);
            writer.Write(evt.SelfId);
            writer.Write(evt.EventAmount);
            foreach (var eventId in evt.EventIds)
            {
                writer.Write(eventId);
            }
            long endPos = writer.BaseStream.Position;
        }
        
        private static void WriteRandomContainerObject(BinaryWriter writer, RandomContainer obj, uint bnkVersion)
        {
            if (obj.RawData != null && obj.RawData.Length > 0)
            {
                writer.Write(obj.RawData);
                return;
            }
            writer.Write((byte)5); // Type 5
            uint length = CalculateRandomContainerObjectSize(obj, bnkVersion);
            writer.Write(length);
            writer.Write(obj.SelfId);
            WriteBaseParams(writer, bnkVersion);
            writer.Write(new byte[24]);
            writer.Write(obj.SoundIdAmount);
            foreach (var id in obj.SoundIds)
                writer.Write(id);
            // Minimal, non-spammy log for affected/new SoundIds
            foreach (var id in obj.SoundIds)
                if (_affectedFileIds.Contains(id))
                    BnkLogger.LogInfo($"[Write] RandomContainer for affected SoundId: {id}");
        }
        
        private static void WriteSwitchContainerObject(BinaryWriter writer, SwitchContainer switchContainer, uint bnkVersion)
        {
            if (switchContainer.RawData != null && switchContainer.RawData.Length > 0)
            {
                writer.Write(switchContainer.RawData);
                return;
            }
            long startPos = writer.BaseStream.Position;
            writer.Write((byte)6); // Type 6
            uint length = CalculateSwitchContainerObjectSize(switchContainer, bnkVersion);
            writer.Write(length);
            writer.Write(switchContainer.SelfId);
            writer.Write(switchContainer.ParentId);
            writer.Write(switchContainer.GroupType);
            writer.Write(switchContainer.GroupId);
            writer.Write(switchContainer.NumChildren);
            foreach (var child in switchContainer.Children)
            {
                writer.Write(child);
            }
            long endPos = writer.BaseStream.Position;
        }
        
        private static void WriteMusicContainerObject(BinaryWriter writer, MusicContainer musicContainer, uint bnkVersion, byte type)
        {
            if (musicContainer.RawData != null && musicContainer.RawData.Length > 0)
            {
                writer.Write(musicContainer.RawData);
                return;
            }
            long startPos = writer.BaseStream.Position;
            writer.Write(type);
            uint length = CalculateMusicContainerObjectSize(musicContainer, bnkVersion);
            writer.Write(length);
            writer.Write(musicContainer.SelfId);
            writer.Write(musicContainer.MusicSwitchId);
            writer.Write(musicContainer.SoundObjectId);
            writer.Write(musicContainer.MusicTrackIdAmount);
            foreach (var trackId in musicContainer.MusicTrackIds)
            {
                writer.Write(trackId);
            }
            long endPos = writer.BaseStream.Position;
        }
        
        private static void WriteMusicTrackObject(BinaryWriter writer, MusicTrack musicTrack, uint bnkVersion)
        {
            if (musicTrack.RawData != null && musicTrack.RawData.Length > 0)
            {
                writer.Write(musicTrack.RawData);
                return;
            }
            long startPos = writer.BaseStream.Position;
            writer.Write((byte)11); // Type 11
            uint length = CalculateMusicTrackObjectSize(musicTrack, bnkVersion);
            writer.Write(length);
            writer.Write(musicTrack.SelfId);
            writer.Write(musicTrack.TrackCount);
            foreach (var fileId in musicTrack.FileIds)
            {
                writer.Write(fileId);
            }
            writer.Write(musicTrack.HasSwitchIds);
            writer.Write(musicTrack.SwitchGroupId);
            if (musicTrack.HasSwitchIds)
            {
                foreach (var switchId in musicTrack.SwitchIds)
                {
                    writer.Write(switchId);
                }
            }
            long endPos = writer.BaseStream.Position;
        }
        
        private static void WriteMusicSwitchObject(BinaryWriter writer, MusicSwitch musicSwitch, uint bnkVersion)
        {
            if (musicSwitch.RawData != null && musicSwitch.RawData.Length > 0)
            {
                writer.Write(musicSwitch.RawData);
                return;
            }
            long startPos = writer.BaseStream.Position;
            writer.Write((byte)12); // Type 12
            uint length = CalculateMusicSwitchObjectSize(musicSwitch, bnkVersion);
            writer.Write(length);
            writer.Write(musicSwitch.SelfId);
            writer.Write(musicSwitch.ParentId);
            writer.Write(musicSwitch.NumChildren);
            foreach (var child in musicSwitch.Children)
            {
                writer.Write(child);
            }
            writer.Write(musicSwitch.NumArguments);
            foreach (var argument in musicSwitch.Arguments)
            {
                writer.Write(argument.GroupId);
                writer.Write(argument.GroupType);
            }
            writer.Write(musicSwitch.NumNodes);
            foreach (var node in musicSwitch.Nodes)
            {
                writer.Write(node.Key);
                writer.Write(node.AudioId);
            }
            long endPos = writer.BaseStream.Position;
        }

        public static bool WriteBnkFile_FullFidelity(BnkFullFidelity fullFidelity, string outputPath)
        {
            BnkLogger.LogOperationStart("Write BNK File (Full Fidelity)");
            try
            {
                BnkLogger.LogInfo($"Writing BNK file to: {outputPath}");
                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(fs))
                {
                    // Group objects by type (sections vs HIRC objects)
                    var sections = new List<BnkObjectRecord>();
                    var hircObjects = new List<BnkObjectRecord>();
                    
                    foreach (var obj in fullFidelity.AllObjectsInOrder)
                    {
                        if (obj.Type == 0xFF) // Section blocks
                        {
                            sections.Add(obj);
                        }
                        else // HIRC objects
                        {
                            hircObjects.Add(obj);
                        }
                    }
                    
                    // Write sections in order (BKHD, DIDX, DATA, etc.)
                    foreach (var section in sections)
                    {
                        writer.Write(section.RawData);
                    }
                    
                    // Write HIRC section with all objects
                    if (hircObjects.Count > 0)
                    {
                        // Write HIRC header
                        writer.Write(Encoding.ASCII.GetBytes("HIRC"));
                        
                        // Calculate HIRC section size (4 bytes for count + all object data)
                        uint hircSize = 4; // Count field
                        foreach (var obj in hircObjects)
                        {
                            if (obj.IsChanged)
                            {
                                // For changed objects, calculate the serialized size
                                byte[] serialized = SerializeObject(obj, fullFidelity.BnkVersion);
                                hircSize += (uint)serialized.Length;
                            }
                            else
                            {
                                // For unchanged objects, RawData includes type + length + data
                                hircSize += (uint)obj.RawData.Length;
                            }
                        }
                        BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] Writing HIRC section: {hircObjects.Count} objects, size={hircSize} bytes");
                        writer.Write(hircSize);
                        
                        // Write object count
                        writer.Write((uint)hircObjects.Count);
                        
                        // Write all HIRC objects
                        int hircObjIndex = 0;
                        foreach (var obj in hircObjects)
                        {
                            int expectedLen = 5 + (int)obj.Length;
                            if (!obj.IsChanged && (obj.RawData == null || obj.RawData.Length < expectedLen))
                            {
                                BnkLogger.LogError($"[WriteBnkFile_FullFidelity] HIRC object {hircObjIndex}: Type={obj.Type}, Length={obj.Length}, RawData.Length={(obj.RawData == null ? -1 : obj.RawData.Length)} -- INSUFFICIENT DATA");
                            }
                            else if (!obj.IsChanged)
                            {
                                BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] HIRC object {hircObjIndex}: Type={obj.Type}, Length={obj.Length}, RawData.Length={obj.RawData.Length}");
                            }
                            else
                            {
                                BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] HIRC object {hircObjIndex}: Type={obj.Type}, Length={obj.Length}, RawData.Length={(obj.RawData == null ? -1 : obj.RawData.Length)} -- WILL BE SERIALIZED");
                            }
                            if (!obj.IsChanged)
                            {
                                // Write the object data (type + length + data)
                                writer.Write((byte)obj.Type);
                                writer.Write(obj.Length);
                                // Extract just the object data (skip type and length from RawData)
                                byte[] objData = new byte[obj.Length];
                                if (obj.RawData != null && obj.RawData.Length >= expectedLen)
                                    Array.Copy(obj.RawData, 5, objData, 0, obj.Length);
                                writer.Write(objData);

                                // Log original object details for comparison
                                if (obj.ParsedObject is Sound sound)
                                {
                                    BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] Original Sound {hircObjIndex}: SelfId={sound.SelfId}, FileId={sound.FileId}, Unknown2.Length={sound.Unknown2?.Length}, Unknown3.Length={sound.Unknown3?.Length}, RawData.Length={obj.RawData?.Length}");
                                    // DEBUG: Log hex dump of the written data for new Sound objects
                                    if (obj.RawData != null && obj.RawData.Length >= 21)
                                    {
                                        BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] [DEBUG] Sound {hircObjIndex} RawData (first 40 bytes): {BitConverter.ToString(obj.RawData, 0, Math.Min(40, obj.RawData.Length)).Replace("-", " ")}");
                                        uint selfIdFromRaw = BitConverter.ToUInt32(obj.RawData, 5);
                                        uint field17FromRaw = BitConverter.ToUInt32(obj.RawData, 17);
                                        BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] [DEBUG] Sound {hircObjIndex}: RawData[5-8]={selfIdFromRaw}, RawData[17-20]={field17FromRaw}");
                                    }
                                }
                                else if (obj.ParsedObject is EventAction eventAction)
                                {
                                    BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] Original EventAction {hircObjIndex}: SelfId={eventAction.SelfId}, Type={eventAction.Type}, SoundObjectId={eventAction.SoundObjectId}, RawData.Length={obj.RawData?.Length}");
                                }
                            }
                            else
                            {
                                // Serialize from ParsedObject
                                BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] Serializing changed object {hircObjIndex}: Type={obj.Type}, ParsedObject={obj.ParsedObject?.GetType().Name ?? "null"}");
                                byte[] newBytes = SerializeObject(obj, fullFidelity.BnkVersion);
                                BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] Serialized object {hircObjIndex}: {newBytes.Length} bytes");
                                
                                // Log detailed comparison for new objects
                                if (obj.ParsedObject is Sound sound)
                                {
                                    BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] New Sound {hircObjIndex}: SelfId={sound.SelfId}, FileId={sound.FileId}, Unknown2.Length={sound.Unknown2?.Length}, Unknown3.Length={sound.Unknown3?.Length}, SerializedSize={newBytes.Length}");
                                }
                                else if (obj.ParsedObject is EventAction eventAction)
                                {
                                    BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] New EventAction {hircObjIndex}: SelfId={eventAction.SelfId}, Type={eventAction.Type}, SoundObjectId={eventAction.SoundObjectId}, SerializedSize={newBytes.Length}");
                                }
                                
                                writer.Write(newBytes);
                                BnkLogger.LogInfo($"[WriteBnkFile_FullFidelity] Wrote {newBytes.Length} bytes for object {hircObjIndex} at position {writer.BaseStream.Position}");
                            }
                            hircObjIndex++;
                        }
                    }
                }
                BnkLogger.LogInfo($"Successfully wrote BNK file: {outputPath}");
                BnkLogger.LogOperationEnd("Write BNK File (Full Fidelity)", true);
                return true;
            }
            catch (Exception ex)
            {
                BnkLogger.LogError($"Failed to write BNK file: {ex.Message}");
                BnkLogger.LogOperationEnd("Write BNK File (Full Fidelity)", false);
                return false;
            }
        }

        // Helper to serialize known types from ParsedObject (expand as needed)
        private static byte[] SerializeObject(BnkObjectRecord obj, uint bnkVersion)
        {
            BnkLogger.LogInfo($"[SerializeObject] Serializing object: Type={obj.Type}, ParsedObject={obj.ParsedObject?.GetType().Name ?? "null"}, BNK version={bnkVersion:X}");
            
            // For now, just return RawData for unknowns or not implemented
            if (obj.ParsedObject == null)
            {
                BnkLogger.LogWarning($"[SerializeObject] ParsedObject is null, returning RawData");
                return obj.RawData;
            }
                
            // Handle Sound objects (Type 2)
            if (obj.ParsedObject is Sound sound)
            {
                BnkLogger.LogInfo($"[SerializeObject] Serializing Sound: SelfId={sound.SelfId}, FileId={sound.FileId}");
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write((byte)2); // Type 2 = Sound
                    
                    // Calculate correct length for Sound based on BNK version and announcer fields
                    uint length = (uint)(4 + 4 + 1 + 4); // SelfId + Unknown1 + IsStreamed + FileId
                    if (bnkVersion <= 0x59 && sound.Unknown2 != null && sound.Unknown2.Length > 0)
                        length += (uint)sound.Unknown2.Length;
                    if (bnkVersion <= 0x70 && sound.Unknown3 != null && sound.Unknown3.Length > 0)
                        length += (uint)sound.Unknown3.Length;
                    
                    // Add length for extended Sound fields if they exist
                    if (sound.SoundStructure != null)
                    {
                        length += 4; // Reserved0
                        length += 4; // StreamSourceType (int, not string!)
                        length += 4; // StreamFileID
                        length += 4; // StreamSourceID

                        // If embedded (StreamSourceType == 0), add offset/length fields
                        if (sound.StreamSourceType == 0)
                        {
                            length += 4; // StreamFileOffset
                            length += 4; // StreamFileLength
                        }

                        length += 1; // SoundType (byte, not string!)

                        // SoundStructure fields (correct byte sizes matching Wwise BNK format)
                        length += 4; // PluginID (uint)
                        length += 2; // Type (ushort)
                        length += 2; // Company (ushort)
                        length += 1; // StreamType (byte)
                        length += 4; // SourceID (int)
                        length += 4; // InMemoryMediaSize (int)
                        length += 1; // SourceBits (byte) - contains IsLanguageSpecific, Prefetch, NonCachable, HasSource as bits
                        length += 1; // BIsOverrideParentFX (byte)
                        length += 1; // NumFx (byte)
                        length += 1; // BIsOverrideParentMetadata (byte)
                        length += 1; // NumPluginMetadata (byte)
                        length += 1; // BOverrideAttachmentParams (byte)
                        length += 4; // OverrideBusID (uint)
                        length += 4; // DirectParentID (uint)
                    }
                    
                    writer.Write(length);
                    writer.Write(sound.SelfId);
                    writer.Write(sound.Unknown1);
                    writer.Write(sound.IsStreamed);
                    if (bnkVersion <= 0x59 && sound.Unknown2 != null && sound.Unknown2.Length > 0)
                        writer.Write(sound.Unknown2);
                    if (bnkVersion <= 0x70 && sound.Unknown3 != null && sound.Unknown3.Length > 0)
                        writer.Write(sound.Unknown3);
                    writer.Write(sound.FileId);
                    
                    // Write extended Sound fields if they exist
                    if (sound.SoundStructure != null)
                    {
                        writer.Write(sound.Reserved0);                         // 4 bytes
                        writer.Write(sound.StreamSourceType);                  // 4 bytes (int: 0=Embedded, 1=Streamed, 2=Streamed ZeroLatency)
                        writer.Write(sound.StreamFileID);                      // 4 bytes
                        writer.Write(sound.StreamSourceID);                    // 4 bytes

                        // If embedded (StreamSourceType == 0), write offset/length fields
                        if (sound.StreamSourceType == 0)
                        {
                            writer.Write(sound.StreamFileOffset);              // 4 bytes
                            writer.Write(sound.StreamFileLength);              // 4 bytes
                        }

                        writer.Write(sound.SoundType);                         // 1 byte (0=SFX, 1=Voice)

                        // Write SoundStructure fields (correct byte sizes matching Wwise BNK format)
                        writer.Write(sound.SoundStructure.PluginID);           // 4 bytes (uint)
                        writer.Write(sound.SoundStructure.Type);               // 2 bytes (ushort)
                        writer.Write(sound.SoundStructure.Company);            // 2 bytes (ushort)
                        writer.Write(sound.SoundStructure.StreamType);         // 1 byte
                        writer.Write(sound.SoundStructure.SourceID);           // 4 bytes (int)
                        writer.Write(sound.SoundStructure.InMemoryMediaSize);  // 4 bytes (int)
                        writer.Write(sound.SoundStructure.SourceBits);         // 1 byte (bitfield containing flags)
                        writer.Write(sound.SoundStructure.BIsOverrideParentFX);      // 1 byte
                        writer.Write(sound.SoundStructure.NumFx);                     // 1 byte
                        writer.Write(sound.SoundStructure.BIsOverrideParentMetadata); // 1 byte
                        writer.Write(sound.SoundStructure.NumPluginMetadata);         // 1 byte
                        writer.Write(sound.SoundStructure.BOverrideAttachmentParams); // 1 byte
                        writer.Write(sound.SoundStructure.OverrideBusID);             // 4 bytes (uint)
                        writer.Write(sound.SoundStructure.DirectParentID);            // 4 bytes (uint)
                    }
                    
                    var result = ms.ToArray();
                    BnkLogger.LogInfo($"[SerializeObject] Serialized Sound: {result.Length} bytes (length field: {length})");
                    return result;
                }
            }
            
            // Handle EventAction objects (Type 3)
            if (obj.ParsedObject is EventAction eventAction)
            {
                BnkLogger.LogInfo($"[SerializeObject] Serializing EventAction: SelfId={eventAction.SelfId}, SoundObjectId={eventAction.SoundObjectId}, Type={eventAction.Type}");
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write((byte)3); // Type 3 = EventAction
                    
                    // Calculate correct length for EventAction
                    uint length = (uint)(4 + 1 + 1 + 4 + 4); // SelfId + Scope + Type + SoundObjectId + SwitchGroupId
                    
                    // Handle different EventAction types based on the reading logic
                    if (eventAction.Type == 25 || eventAction.Type == 18)
                    {
                        length += 5; // 5 bytes of padding
                        length += 64; // Base params size
                        length += 4; // SwitchGroupId (written before SoundObjectId for these types)
                        length += 4; // SoundObjectId
                    }
                    
                    writer.Write(length);
                    writer.Write(eventAction.SelfId);
                    writer.Write(eventAction.Scope);
                    writer.Write(eventAction.Type);
                    
                    // Handle different EventAction types based on the reading logic
                    if (eventAction.Type == 25 || eventAction.Type == 18)
                    {
                        // Write 5 bytes of padding
                        writer.Write(new byte[5]);
                        // Write base params (simplified - just write zeros for now)
                        writer.Write(new byte[64]); // Base params size
                        writer.Write(eventAction.SwitchGroupId);
                        writer.Write(eventAction.SoundObjectId);
                    }
                    else
                    {
                        writer.Write(eventAction.SoundObjectId);
                        writer.Write(eventAction.SwitchGroupId);
                    }
                    
                    var result = ms.ToArray();
                    BnkLogger.LogInfo($"[SerializeObject] Serialized EventAction: {result.Length} bytes (length field: {length})");
                    return result;
                }
            }
            
            // Handle Event objects (Type 4) - for when events are modified
            if (obj.ParsedObject is Event evt)
            {
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write((byte)4); // Type 4 = Event

                    // Calculate correct length for Event
                    uint length = (uint)(4 + 1 + 4 * evt.EventIds.Count); // SelfId + EventAmount + EventIds

                    writer.Write(length);
                    writer.Write(evt.SelfId);
                    writer.Write(evt.EventAmount);
                    foreach (var eventId in evt.EventIds)
                    {
                        writer.Write(eventId);
                    }
                    return ms.ToArray();
                }
            }

            // Handle RandomContainer objects (Type 5) - for when containers are modified
            if (obj.ParsedObject is RandomContainer randomContainer)
            {
                BnkLogger.LogInfo($"[SerializeObject] Serializing RandomContainer: SelfId={randomContainer.SelfId}, SoundIdAmount={randomContainer.SoundIdAmount}, SoundIds.Count={randomContainer.SoundIds.Count}");
                BnkLogger.LogInfo($"[SerializeObject] [DEBUG] RandomContainer obj.RawData={obj.RawData?.Length ?? -1}, randomContainer.RawData={randomContainer.RawData?.Length ?? -1}");

                // Use the original RawData and patch only the playlist section at the end
                // Structure: Type (1) + Length (4) + SelfId (4) + BaseParams (variable) + 24 bytes + SoundIdAmount (4) + Sound IDs (N * 4 bytes each)
                byte[] originalData = obj.RawData ?? randomContainer.RawData;
                if (originalData == null || originalData.Length < 5)
                {
                    BnkLogger.LogError($"[SerializeObject] RandomContainer has no RawData, cannot serialize");
                    return new byte[0];
                }
                BnkLogger.LogInfo($"[SerializeObject] [DEBUG] Using originalData with {originalData.Length} bytes");

                // Parse the original to find the playlist position by re-reading
                using (var ms = new MemoryStream(originalData))
                using (var reader = new BinaryReader(ms))
                {
                    byte type = reader.ReadByte(); // Should be 5
                    uint origLength = reader.ReadUInt32();
                    uint selfId = reader.ReadUInt32();

                    // Skip BaseParams using the helper (same as during parsing)
                    uint dummy;
                    BnkBinaryHelpers.SkipBaseParams(reader, bnkVersion, out dummy);

                    // Skip the 24 bytes after BaseParams
                    reader.BaseStream.Seek(24, SeekOrigin.Current);

                    // Now we're at SoundIdAmount position
                    long soundIdAmountPos = reader.BaseStream.Position;
                    uint originalCount = reader.ReadUInt32();

                    // Playlist is just 4 bytes per Sound ID (not 12 bytes!)
                    long oldPlaylistEnd = soundIdAmountPos + 4 + (originalCount * 4);
                    long bytesRemaining = originalData.Length - oldPlaylistEnd;

                    BnkLogger.LogInfo($"[SerializeObject] Found SoundIdAmount at position {soundIdAmountPos}, original count={originalCount}, trailing bytes={bytesRemaining}");

                    // Build new RawData
                    using (var outMs = new MemoryStream())
                    using (var writer = new BinaryWriter(outMs))
                    {
                        // Copy everything up to (but not including) SoundIdAmount
                        writer.Write(originalData, 0, (int)soundIdAmountPos);

                        // Write new SoundIdAmount and Sound IDs (4 bytes each, not 12!)
                        writer.Write((uint)randomContainer.SoundIds.Count);
                        BnkLogger.LogInfo($"[SerializeObject] Writing SoundIdAmount={randomContainer.SoundIds.Count}");
                        foreach (var soundId in randomContainer.SoundIds)
                        {
                            writer.Write(soundId);
                            BnkLogger.LogInfo($"[SerializeObject] Wrote Sound ID: {soundId}");
                        }

                        // Copy any trailing bytes
                        if (bytesRemaining > 0)
                        {
                            writer.Write(originalData, (int)oldPlaylistEnd, (int)bytesRemaining);
                            BnkLogger.LogInfo($"[SerializeObject] Copied {bytesRemaining} trailing bytes from position {oldPlaylistEnd}");
                        }

                        // Update the length field (bytes 1-4)
                        byte[] newData = outMs.ToArray();
                        uint newLength = (uint)(newData.Length - 5); // Exclude type byte and length field itself
                        Array.Copy(BitConverter.GetBytes(newLength), 0, newData, 1, 4);

                        BnkLogger.LogInfo($"[SerializeObject] Serialized RandomContainer: {newData.Length} bytes (old: {originalData.Length}, length field: {newLength})");
                        return newData;
                    }
                }
            }

            // For any other types, return RawData
            return obj.RawData;
        }

        // Test: Write the original BNK file bytes directly to output, no modification
        public static bool WriteBnkFile_CopyRaw(byte[] originalBytes, string outputPath)
        {
            try
            {
                BnkWpkLogger.LogInfo($"WriteBnkFile_CopyRaw: Starting write operation");
                BnkWpkLogger.LogInfo($"WriteBnkFile_CopyRaw: Input bytes: {originalBytes?.Length ?? 0}");
                BnkWpkLogger.LogInfo($"WriteBnkFile_CopyRaw: Output path: {outputPath}");
                
                // Validate input
                if (originalBytes == null)
                {
                    BnkWpkLogger.LogError("WriteBnkFile_CopyRaw: originalBytes is null");
                    return false;
                }
                
                if (originalBytes.Length == 0)
                {
                    BnkWpkLogger.LogError("WriteBnkFile_CopyRaw: originalBytes is empty (0 bytes)");
                    return false;
                }
                
                if (string.IsNullOrEmpty(outputPath))
                {
                    BnkWpkLogger.LogError("WriteBnkFile_CopyRaw: outputPath is null or empty");
                    return false;
                }
                
                // Check if output directory exists
                string outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    BnkWpkLogger.LogInfo($"WriteBnkFile_CopyRaw: Creating output directory: {outputDir}");
                    Directory.CreateDirectory(outputDir);
                }
                
                // Check if output file is writable (if it exists)
                if (File.Exists(outputPath))
                {
                    BnkWpkLogger.LogInfo("WriteBnkFile_CopyRaw: Output file exists, checking if writable");
                    try
                    {
                        using (var testStream = File.Open(outputPath, FileMode.Open, FileAccess.Write))
                        {
                            testStream.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        BnkWpkLogger.LogError($"WriteBnkFile_CopyRaw: Output file is not writable: {ex.Message}");
                        return false;
                    }
                }
                
                BnkWpkLogger.LogInfo("WriteBnkFile_CopyRaw: Creating FileStream");
                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    BnkWpkLogger.LogInfo($"WriteBnkFile_CopyRaw: FileStream created, position: {fs.Position}");
                    BnkWpkLogger.LogInfo($"WriteBnkFile_CopyRaw: About to write {originalBytes.Length} bytes");
                    
                    fs.Write(originalBytes, 0, originalBytes.Length);
                    
                    BnkWpkLogger.LogInfo($"WriteBnkFile_CopyRaw: Write completed, position: {fs.Position}");
                    
                    // Force flush to ensure data is written to disk
                    fs.Flush();
                    BnkWpkLogger.LogInfo("WriteBnkFile_CopyRaw: Flush completed");
                }
                
                // Verify the file was written correctly
                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    BnkWpkLogger.LogInfo($"WriteBnkFile_CopyRaw: File exists after write, size: {fileInfo.Length} bytes");
                    
                    if (fileInfo.Length == 0)
                    {
                        BnkWpkLogger.LogError("WriteBnkFile_CopyRaw: File exists but is 0 bytes - write failed");
                        return false;
                    }
                    
                    if (fileInfo.Length != originalBytes.Length)
                    {
                        BnkWpkLogger.LogWarning($"WriteBnkFile_CopyRaw: File size ({fileInfo.Length}) differs from expected ({originalBytes.Length})");
                    }
                }
                else
                {
                    BnkWpkLogger.LogError("WriteBnkFile_CopyRaw: File does not exist after write operation");
                    return false;
                }
                
                BnkWpkLogger.LogInfo($"WriteBnkFile_CopyRaw: Successfully wrote {originalBytes.Length} bytes to {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                BnkWpkLogger.LogError($"WriteBnkFile_CopyRaw: Exception during write: {ex.GetType().Name}: {ex.Message}");
                BnkWpkLogger.LogError($"WriteBnkFile_CopyRaw: Stack trace: {ex.StackTrace}");
                BnkWpkLogger.LogError($"WriteBnkFile_CopyRaw: Input bytes: {originalBytes?.Length ?? 0}, Output path: {outputPath}");
                return false;
            }
        }

        // Validate that a written BNK file can be parsed correctly
        public static bool ValidateWrittenBnkFile(string bnkPath)
        {
            try
            {
                BnkLogger.LogInfo($"[ValidateWrittenBnkFile] Validating BNK file: {bnkPath}");
                
                if (!File.Exists(bnkPath))
                {
                    BnkLogger.LogError($"[ValidateWrittenBnkFile] File does not exist: {bnkPath}");
                    return false;
                }
                
                var fileInfo = new FileInfo(bnkPath);
                if (fileInfo.Length == 0)
                {
                    BnkLogger.LogError($"[ValidateWrittenBnkFile] File is empty: {bnkPath}");
                    return false;
                }
                
                BnkLogger.LogInfo($"[ValidateWrittenBnkFile] File size: {fileInfo.Length} bytes");
                
                // Try to parse the written file
                var testFullFidelity = new BnkFullFidelity();
                var testSections = new BNKSections();
                bool parseSuccess = BnkExtractor.ParseEventBnkFile_FullFidelity(bnkPath, testFullFidelity, testSections);
                
                if (!parseSuccess)
                {
                    BnkLogger.LogError($"[ValidateWrittenBnkFile] Failed to parse written BNK file: {bnkPath}");
                    return false;
                }
                
                BnkLogger.LogInfo($"[ValidateWrittenBnkFile] Successfully parsed written BNK file:");
                BnkLogger.LogInfo($"[ValidateWrittenBnkFile]   BNK Version: {testFullFidelity.BnkVersion:X}");
                BnkLogger.LogInfo($"[ValidateWrittenBnkFile]   Total Objects: {testFullFidelity.AllObjectsInOrder.Count}");
                BnkLogger.LogInfo($"[ValidateWrittenBnkFile]   Sounds: {testSections.Sounds.Count}");
                BnkLogger.LogInfo($"[ValidateWrittenBnkFile]   EventActions: {testSections.EventActions.Count}");
                BnkLogger.LogInfo($"[ValidateWrittenBnkFile]   Events: {testSections.Events.Count}");
                
                // Check for any new objects (those with high IDs)
                var newSounds = testSections.Sounds.Where(s => s.SelfId > 1000).ToList();
                var newEventActions = testSections.EventActions.Where(ea => ea.SelfId > 1000).ToList();
                
                if (newSounds.Count > 0)
                {
                    BnkLogger.LogInfo($"[ValidateWrittenBnkFile] Found {newSounds.Count} new Sound objects:");
                    foreach (var sound in newSounds)
                    {
                        BnkLogger.LogInfo($"[ValidateWrittenBnkFile]   New Sound: SelfId={sound.SelfId}, FileId={sound.FileId}, Unknown2.Length={sound.Unknown2?.Length}, Unknown3.Length={sound.Unknown3?.Length}");
                    }
                }
                
                if (newEventActions.Count > 0)
                {
                    BnkLogger.LogInfo($"[ValidateWrittenBnkFile] Found {newEventActions.Count} new EventAction objects:");
                    foreach (var ea in newEventActions)
                    {
                        BnkLogger.LogInfo($"[ValidateWrittenBnkFile]   New EventAction: SelfId={ea.SelfId}, Type={ea.Type}, SoundObjectId={ea.SoundObjectId}");
                    }
                }
                
                BnkLogger.LogInfo($"[ValidateWrittenBnkFile] BNK file validation successful: {bnkPath}");
                return true;
            }
            catch (Exception ex)
            {
                BnkLogger.LogError($"[ValidateWrittenBnkFile] Exception during validation: {ex.Message}");
                BnkLogger.LogError($"[ValidateWrittenBnkFile] Stack trace: {ex.StackTrace}");
                return false;
            }
        }
    }
} 