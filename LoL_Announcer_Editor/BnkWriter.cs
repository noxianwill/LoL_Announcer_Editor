using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace LoL_Announcer_Editor
{
    public static class BnkWriter
    {
        public static bool WriteBnkFile(BNKSections sections, string outputPath, uint bnkVersion = 0x86)
        {
            try
            {
                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(fs))
                {
                    // Write BKHD section
                    WriteBkhdSection(writer, bnkVersion);

                    // Write HIRC section
                    WriteHircSection(writer, sections, bnkVersion);
                }

                return true;
            }
            catch
            {
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
        }

        private static void WriteHircSection(BinaryWriter writer, BNKSections sections, uint bnkVersion)
        {
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

            writer.Write(Encoding.ASCII.GetBytes("HIRC"));
            writer.Write(hircSize);
            uint totalObjects = (uint)(sections.Sounds.Count + sections.EventActions.Count + sections.Events.Count +
                sections.RandomContainers.Count + sections.SwitchContainers.Count + sections.MusicSegments.Count +
                sections.MusicTracks.Count + sections.MusicSwitches.Count + sections.MusicPlaylists.Count);
            writer.Write(totalObjects);

            foreach (var sound in sections.Sounds)
                WriteSoundObject(writer, sound, bnkVersion);
            foreach (var ea in sections.EventActions)
                WriteEventActionObject(writer, ea);
            foreach (var evt in sections.Events)
                WriteEventObject(writer, evt, bnkVersion);
            foreach (var rc in sections.RandomContainers)
                WriteRandomContainerObject(writer, rc, bnkVersion);
            foreach (var sc in sections.SwitchContainers)
                WriteSwitchContainerObject(writer, sc, bnkVersion);
            foreach (var ms in sections.MusicSegments)
                WriteMusicContainerObject(writer, ms, bnkVersion, 10);
            foreach (var mt in sections.MusicTracks)
                WriteMusicTrackObject(writer, mt, bnkVersion);
            foreach (var msi in sections.MusicSwitches)
                WriteMusicSwitchObject(writer, msi, bnkVersion);
            foreach (var mpl in sections.MusicPlaylists)
                WriteMusicContainerObject(writer, mpl, bnkVersion, 13);
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
        }

        private static void WriteEventActionObject(BinaryWriter writer, EventAction eventAction)
        {
            if (eventAction.RawData != null && eventAction.RawData.Length > 0)
            {
                writer.Write(eventAction.RawData);
                return;
            }
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
        }

        private static void WriteEventObject(BinaryWriter writer, Event evt, uint bnkVersion)
        {
            if (evt.RawData != null && evt.RawData.Length > 0)
            {
                writer.Write(evt.RawData);
                return;
            }
            writer.Write((byte)4); // Type 4
            uint length = CalculateEventObjectSize(evt, bnkVersion);
            writer.Write(length);
            writer.Write(evt.SelfId);
            writer.Write(evt.EventAmount);
            foreach (var eventId in evt.EventIds)
            {
                writer.Write(eventId);
            }
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
        }

        private static void WriteSwitchContainerObject(BinaryWriter writer, SwitchContainer switchContainer, uint bnkVersion)
        {
            if (switchContainer.RawData != null && switchContainer.RawData.Length > 0)
            {
                writer.Write(switchContainer.RawData);
                return;
            }
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
        }

        private static void WriteMusicContainerObject(BinaryWriter writer, MusicContainer musicContainer, uint bnkVersion, byte type)
        {
            if (musicContainer.RawData != null && musicContainer.RawData.Length > 0)
            {
                writer.Write(musicContainer.RawData);
                return;
            }
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
        }

        private static void WriteMusicTrackObject(BinaryWriter writer, MusicTrack musicTrack, uint bnkVersion)
        {
            if (musicTrack.RawData != null && musicTrack.RawData.Length > 0)
            {
                writer.Write(musicTrack.RawData);
                return;
            }
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
        }

        private static void WriteMusicSwitchObject(BinaryWriter writer, MusicSwitch musicSwitch, uint bnkVersion)
        {
            if (musicSwitch.RawData != null && musicSwitch.RawData.Length > 0)
            {
                writer.Write(musicSwitch.RawData);
                return;
            }
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
        }

        public static bool WriteBnkFile_FullFidelity(BnkFullFidelity fullFidelity, string outputPath)
        {
            try
            {
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
                        writer.Write(hircSize);

                        // Write object count
                        writer.Write((uint)hircObjects.Count);

                        // Write all HIRC objects
                        foreach (var obj in hircObjects)
                        {
                            int expectedLen = 5 + (int)obj.Length;
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
                            }
                            else
                            {
                                // Serialize from ParsedObject
                                byte[] newBytes = SerializeObject(obj, fullFidelity.BnkVersion);
                                writer.Write(newBytes);
                            }
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Helper to serialize known types from ParsedObject (expand as needed)
        private static byte[] SerializeObject(BnkObjectRecord obj, uint bnkVersion)
        {
            // For now, just return RawData for unknowns or not implemented
            if (obj.ParsedObject == null)
            {
                return obj.RawData;
            }

            // Handle Sound objects (Type 2)
            if (obj.ParsedObject is Sound sound)
            {
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

                    return ms.ToArray();
                }
            }

            // Handle EventAction objects (Type 3)
            if (obj.ParsedObject is EventAction eventAction)
            {
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

                    return ms.ToArray();
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
                WemAdditionLogger.LogSection("BnkWriter.SerializeObject - RandomContainer");
                WemAdditionLogger.LogRandomContainer(randomContainer, "SERIALIZING");

                // Use the original RawData and patch BOTH children array AND playlist section
                // Structure according to Python parser (parse_hirc_sequence_container):
                // Type (1) + Length (4) + SelfId (4) + NodeBaseParams (variable) + 24 bytes +
                // ChildrenCount (4) + Children (N * 4) + PlaylistLength (2) + Playlist (M * 8)
                byte[] originalData = obj.RawData ?? randomContainer.RawData;
                if (originalData == null || originalData.Length < 5)
                {
                    WemAdditionLogger.Log("ERROR: RandomContainer has no RawData");
                    return new byte[0];
                }
                WemAdditionLogger.Log($"Original RawData length: {originalData.Length}");

                // Parse the original to find the children and playlist positions
                using (var ms = new MemoryStream(originalData))
                using (var reader = new BinaryReader(ms))
                {
                    byte type = reader.ReadByte(); // Should be 5
                    uint origLength = reader.ReadUInt32();
                    uint selfId = reader.ReadUInt32();
                    WemAdditionLogger.Log($"Original: Type={type}, Length={origLength}, SelfId={selfId}");

                    // Skip BaseParams using the helper (same as during parsing)
                    uint dummy;
                    BnkBinaryHelpers.SkipBaseParams(reader, bnkVersion, out dummy);

                    // Skip the 24 bytes after BaseParams (loop info, transition, etc.)
                    reader.BaseStream.Seek(24, SeekOrigin.Current);

                    // Record position of children count
                    long childrenCountPos = reader.BaseStream.Position;
                    uint originalChildrenCount = reader.ReadUInt32();
                    long childrenArrayStart = reader.BaseStream.Position;
                    reader.BaseStream.Seek(originalChildrenCount * 4, SeekOrigin.Current);
                    long childrenArrayEnd = reader.BaseStream.Position;
                    WemAdditionLogger.Log($"Children: count={originalChildrenCount}, pos={childrenCountPos}, arrayEnd={childrenArrayEnd}");

                    // Now we're at PlaylistLength position (u16, NOT u32!)
                    long playlistLengthPos = reader.BaseStream.Position;
                    ushort originalPlaylistLength = reader.ReadUInt16();
                    WemAdditionLogger.Log($"PlaylistLength position: {playlistLengthPos}, Original playlist length: {originalPlaylistLength}");

                    // Original playlist is 8 bytes per entry (playID: u32 + weight: u32)
                    long oldPlaylistEnd = playlistLengthPos + 2 + (originalPlaylistLength * 8);
                    long bytesRemaining = originalData.Length - oldPlaylistEnd;
                    WemAdditionLogger.Log($"Old playlist end: {oldPlaylistEnd}, Bytes remaining after: {bytesRemaining}");

                    // Build new children list from playlist (children = all PlayIDs in playlist)
                    var newChildrenIds = randomContainer.Playlist.Select(p => p.PlayID).ToList();
                    WemAdditionLogger.Log($"New children count: {newChildrenIds.Count}");

                    // Build new RawData
                    using (var outMs = new MemoryStream())
                    using (var writer = new BinaryWriter(outMs))
                    {
                        // Copy everything up to (but not including) ChildrenCount
                        writer.Write(originalData, 0, (int)childrenCountPos);

                        // Write new ChildrenCount (u32)
                        writer.Write((uint)newChildrenIds.Count);

                        // Write new Children array (each u32)
                        foreach (var childId in newChildrenIds)
                        {
                            writer.Write(childId);
                            WemAdditionLogger.Log($"  Writing Child: {childId}");
                        }

                        // Write new PlaylistLength as u16
                        ushort newPlaylistLength = (ushort)randomContainer.Playlist.Count;
                        writer.Write(newPlaylistLength);
                        WemAdditionLogger.Log($"Writing new PlaylistLength: {newPlaylistLength}");

                        // Write playlist entries (8 bytes each: playID + weight)
                        foreach (var entry in randomContainer.Playlist)
                        {
                            writer.Write(entry.PlayID);
                            writer.Write(entry.Weight);
                            WemAdditionLogger.Log($"  Writing PlaylistEntry: PlayID={entry.PlayID}, Weight={entry.Weight}");
                        }

                        // Copy any trailing bytes
                        if (bytesRemaining > 0)
                        {
                            writer.Write(originalData, (int)oldPlaylistEnd, (int)bytesRemaining);
                            WemAdditionLogger.Log($"Copied {bytesRemaining} trailing bytes");
                        }

                        // Update the length field (bytes 1-4)
                        byte[] newData = outMs.ToArray();
                        uint newLength = (uint)(newData.Length - 5); // Exclude type byte and length field itself
                        Array.Copy(BitConverter.GetBytes(newLength), 0, newData, 1, 4);

                        WemAdditionLogger.Log($"New RawData length: {newData.Length}, New length field: {newLength}");
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
                // Validate input
                if (originalBytes == null || originalBytes.Length == 0 || string.IsNullOrEmpty(outputPath))
                {
                    return false;
                }

                // Check if output directory exists
                string? outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(originalBytes, 0, originalBytes.Length);
                    fs.Flush();
                }

                // Verify the file was written correctly
                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    if (fileInfo.Length == 0)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Validate that a written BNK file can be parsed correctly
        public static bool ValidateWrittenBnkFile(string bnkPath)
        {
            try
            {
                if (!File.Exists(bnkPath))
                {
                    return false;
                }

                var fileInfo = new FileInfo(bnkPath);
                if (fileInfo.Length == 0)
                {
                    return false;
                }

                // Try to parse the written file
                var testFullFidelity = new BnkFullFidelity();
                var testSections = new BNKSections();
                bool parseSuccess = BnkExtractor.ParseEventBnkFile_FullFidelity(bnkPath, testFullFidelity, testSections);

                return parseSuccess;
            }
            catch
            {
                return false;
            }
        }
    }
}
