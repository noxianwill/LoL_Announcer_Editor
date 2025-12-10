using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoL_Announcer_Editor
{
    // This file only parses objects from the BNK file itself.
    // It does NOT use any external reference data or JSON for field values.
    // For unchanged objects, RawData is the only source of truth for writing.
    public static class BnkObjectReaders
    {
        public static bool ReadRandomContainerObject(BinaryReader reader, List<RandomContainer> randomContainers, uint bnkVersion, byte[] fullObjectBytes)
        {
            var obj = new RandomContainer();
            obj.SelfId = reader.ReadUInt32();
            uint dummy;
            obj.SwitchContainerId = BnkBinaryHelpers.SkipBaseParams(reader, bnkVersion, out dummy);
            reader.BaseStream.Seek(24, SeekOrigin.Current);
            obj.SoundIdAmount = reader.ReadUInt32();

            // Read full 12-byte playlist entries (playID + weight + rsGroupID)
            for (int i = 0; i < obj.SoundIdAmount; i++)
            {
                var entry = new PlaylistEntry
                {
                    PlayID = reader.ReadUInt32(),
                    Weight = reader.ReadUInt32(),
                    RsGroupID = reader.ReadUInt32()
                };
                obj.Playlist.Add(entry);
                obj.SoundIds.Add(entry.PlayID);  // Maintain backward compatibility
            }

            obj.RawData = fullObjectBytes;
            randomContainers.Add(obj);
            BnkLogger.LogInfo($"[DEBUG] Parsed RandomContainer: SelfId={obj.SelfId}, SoundIdAmount={obj.SoundIdAmount}, Playlist entries=[{string.Join(", ", obj.Playlist.Select(p => $"{{PlayID={p.PlayID}, Weight={p.Weight}, RsGroupID={p.RsGroupID}}}"))}]");
            return true;
        }

        public static bool ReadSwitchContainerObject(BinaryReader reader, List<SwitchContainer> switchContainers, uint bnkVersion, byte[] fullObjectBytes)
        {
            var obj = new SwitchContainer();
            obj.SelfId = reader.ReadUInt32();
            uint dummy;
            obj.ParentId = BnkBinaryHelpers.SkipBaseParams(reader, bnkVersion, out dummy);
            obj.GroupType = reader.ReadByte();
            if (bnkVersion <= 0x59) reader.BaseStream.Seek(3, SeekOrigin.Current);
            obj.GroupId = reader.ReadUInt32();
            reader.BaseStream.Seek(5, SeekOrigin.Current);
            obj.NumChildren = reader.ReadUInt32();
            for (int i = 0; i < obj.NumChildren; i++)
                obj.Children.Add(reader.ReadUInt32());
            obj.RawData = fullObjectBytes;
            switchContainers.Add(obj);
            return true;
        }

        public static bool ReadSoundObject(BinaryReader reader, List<Sound> sounds, uint bnkVersion, byte[] fullObjectBytes)
        {
            var obj = new Sound();
            long objectStartPos = reader.BaseStream.Position;

            obj.SelfId = reader.ReadUInt32();
            obj.Unknown1 = reader.ReadUInt32();
            obj.IsStreamed = reader.ReadByte();
            if (bnkVersion <= 0x59)
                obj.Unknown2 = reader.ReadBytes(3);
            else
                obj.Unknown2 = new byte[0];
            if (bnkVersion <= 0x70)
                obj.Unknown3 = reader.ReadBytes(4);
            else
                obj.Unknown3 = new byte[0];
            obj.FileId = reader.ReadUInt32();

            // Check if there's more data (SoundStructure fields for announcer/voice packs)
            long currentPos = reader.BaseStream.Position;
            long bytesRead = currentPos - objectStartPos;
            long totalBytes = fullObjectBytes.Length - 5; // Exclude type byte (1) + length field (4)
            long remainingBytes = totalBytes - bytesRead;

            // Only try to parse SoundStructure if there's enough remaining data
            // Minimum structure: 4 (reserved0) + 4 (streamSourceType) + 4 (streamFileID) + 4 (streamSourceID) + 1 (soundType) + 31 (SoundStructure) = 48 bytes
            // For embedded sources, add 8 more bytes (streamFileOffset + streamFileLength)
            if (remainingBytes >= 48)
            {
                try
                {
                    // This Sound has extended fields - parse them (following Python parser structure)
                    obj.Reserved0 = reader.ReadInt32();                    // 4 bytes
                    obj.StreamSourceType = reader.ReadInt32();             // 4 bytes: 0=Embedded, 1=Streamed, 2=Streamed(ZeroLatency)
                    obj.StreamFileID = reader.ReadUInt32();                // 4 bytes
                    obj.StreamSourceID = reader.ReadInt32();               // 4 bytes

                    // If embedded (StreamSourceType == 0), read additional offset/length fields
                    if (obj.StreamSourceType == 0)
                    {
                        obj.StreamFileOffset = reader.ReadInt32();         // 4 bytes
                        obj.StreamFileLength = reader.ReadInt32();         // 4 bytes
                    }

                    obj.SoundType = reader.ReadByte();                     // 1 byte: 0=SFX, 1=Voice

                    // Parse SoundStructure nested fields (following Wwise BNK format)
                    obj.SoundStructure = new SoundStructure
                    {
                        PluginID = reader.ReadUInt32(),          // 4 bytes
                        Type = reader.ReadUInt16(),              // 2 bytes
                        Company = reader.ReadUInt16(),           // 2 bytes
                        StreamType = reader.ReadByte(),          // 1 byte
                        SourceID = reader.ReadInt32(),           // 4 bytes
                        InMemoryMediaSize = reader.ReadInt32(),  // 4 bytes
                        SourceBits = reader.ReadByte(),          // 1 byte (bitfield)
                        BIsOverrideParentFX = reader.ReadByte(), // 1 byte
                        NumFx = reader.ReadByte(),               // 1 byte
                        BIsOverrideParentMetadata = reader.ReadByte(), // 1 byte
                        NumPluginMetadata = reader.ReadByte(),   // 1 byte
                        BOverrideAttachmentParams = reader.ReadByte(), // 1 byte
                        OverrideBusID = reader.ReadUInt32(),     // 4 bytes
                        DirectParentID = reader.ReadUInt32()     // 4 bytes
                    };

                    // Parse bitfield flags from SourceBits
                    byte sourceBits = obj.SoundStructure.SourceBits;
                    obj.SoundStructure.IsLanguageSpecific = (sourceBits & 0x01) != 0;
                    obj.SoundStructure.Prefetch = (sourceBits & 0x02) != 0;
                    obj.SoundStructure.NonCachable = (sourceBits & 0x08) != 0;
                    obj.SoundStructure.HasSource = (sourceBits & 0x80) != 0;
                }
                catch (Exception ex)
                {
                    BnkLogger.LogWarning($"[ReadSoundObject] Failed to parse SoundStructure for Sound {obj.SelfId}: {ex.Message}. Remaining bytes: {remainingBytes}");
                    // Continue without SoundStructure - the object will just have basic fields
                }
            }

            // Do NOT overwrite any fields with reference JSON values.
            // For unchanged objects, RawData is the only source of truth.
            obj.RawData = fullObjectBytes;
            sounds.Add(obj);
            return true;
        }

        public static bool ReadEventActionObject(BinaryReader reader, List<EventAction> eventActions, byte[] fullObjectBytes)
        {
            var obj = new EventAction();
            obj.SelfId = reader.ReadUInt32();
            obj.Scope = reader.ReadByte();
            obj.Type = reader.ReadByte();
            if (obj.Type == 25)
            {
                reader.BaseStream.Seek(5, SeekOrigin.Current);
                BnkBinaryHelpers.SkipInitialParams(reader);
                obj.SwitchGroupId = reader.ReadUInt32();
                obj.SoundObjectId = reader.ReadUInt32();
            }
            else if (obj.Type == 18)
            {
                reader.BaseStream.Seek(5, SeekOrigin.Current);
                BnkBinaryHelpers.SkipInitialParams(reader);
                obj.SwitchGroupId = reader.ReadUInt32();
                obj.SoundObjectId = reader.ReadUInt32();
            }
            else
            {
                obj.SoundObjectId = reader.ReadUInt32();
            }
            obj.RawData = fullObjectBytes;
            eventActions.Add(obj);
            return true;
        }

        public static bool ReadEventObject(BinaryReader reader, List<Event> events, uint bnkVersion, byte[] fullObjectBytes)
        {
            var obj = new Event();
            obj.SelfId = reader.ReadUInt32();
            obj.EventAmount = reader.ReadByte();
            if (bnkVersion == 0x58) reader.BaseStream.Seek(3, SeekOrigin.Current);
            for (int i = 0; i < obj.EventAmount; i++)
                obj.EventIds.Add(reader.ReadUInt32());
            obj.RawData = fullObjectBytes;
            events.Add(obj);
            BnkLogger.LogInfo($"[DEBUG] Parsed Event: SelfId={obj.SelfId}, EventAmount={obj.EventAmount}, EventIds=[{string.Join(", ", obj.EventIds)}]");
            return true;
        }

        public static bool ReadMusicContainerObject(BinaryReader reader, List<MusicContainer> musicContainers, uint bnkVersion, byte[] fullObjectBytes)
        {
            var obj = new MusicContainer();
            obj.SelfId = reader.ReadUInt32();
            reader.BaseStream.Seek(1, SeekOrigin.Current);
            uint musicSwitchId;
            obj.SoundObjectId = BnkBinaryHelpers.SkipBaseParams(reader, bnkVersion, out musicSwitchId);
            obj.MusicSwitchId = musicSwitchId;
            obj.MusicTrackIdAmount = reader.ReadUInt32();
            for (int i = 0; i < obj.MusicTrackIdAmount; i++)
                obj.MusicTrackIds.Add(reader.ReadUInt32());
            obj.RawData = fullObjectBytes;
            musicContainers.Add(obj);
            return true;
        }

        public static bool ReadMusicTrackObject(BinaryReader reader, List<MusicTrack> musicTracks, uint bnkVersion, byte[] fullObjectBytes)
        {
            var obj = new MusicTrack();
            obj.SelfId = reader.ReadUInt32();
            reader.ReadByte();
            uint count = reader.ReadUInt32();
            reader.BaseStream.Seek(14 * count, SeekOrigin.Current);
            count = reader.ReadUInt32();
            long pos = reader.BaseStream.Position;
            reader.BaseStream.Seek(count * 40, SeekOrigin.Current);
            obj.TrackCount = reader.ReadUInt32();
            reader.BaseStream.Seek(-4 - (count * 40), SeekOrigin.Current);
            for (int i = 0; i < obj.TrackCount; i++)
                obj.FileIds.Add(0);
            for (int i = 0; i < count; i++)
            {
                var sourceInfo = new TrackSourceInfo
                {
                    TrackIndex = reader.ReadUInt32(),
                    FileId = reader.ReadUInt32(),
                    EventId = reader.ReadUInt32(),
                    PlayAt = reader.ReadDouble(),
                    BeginTrimOffset = reader.ReadDouble(),
                    EndTrimOffset = reader.ReadDouble(),
                    SourceDuration = reader.ReadDouble()
                };
                obj.FileIds[(int)sourceInfo.TrackIndex] = sourceInfo.FileId;
            }
            reader.BaseStream.Seek(4, SeekOrigin.Current);
            BnkBinaryHelpers.SkipClipAutomation(reader);
            obj.ParentId = BnkBinaryHelpers.SkipBaseParams(reader, bnkVersion, out _);
            byte trackType = reader.ReadByte();
            obj.HasSwitchIds = trackType == 0x3;
            if (obj.HasSwitchIds)
            {
                reader.ReadByte();
                obj.SwitchGroupId = reader.ReadUInt32();
                reader.BaseStream.Seek(4, SeekOrigin.Current);
                uint switchCount = reader.ReadUInt32();
                if (switchCount != obj.TrackCount)
                    throw new InvalidDataException("Switch id count does not match track count!");
                for (int i = 0; i < obj.TrackCount; i++)
                    obj.SwitchIds.Add(reader.ReadUInt32());
            }
            obj.RawData = fullObjectBytes;
            musicTracks.Add(obj);
            return true;
        }

        public static bool ReadMusicSwitchObject(BinaryReader reader, List<MusicSwitch> musicSwitches, uint bnkVersion, byte[] fullObjectBytes)
        {
            var obj = new MusicSwitch();
            obj.SelfId = reader.ReadUInt32();
            reader.BaseStream.Seek(1, SeekOrigin.Current);
            obj.ParentId = BnkBinaryHelpers.SkipBaseParams(reader, bnkVersion, out _);
            obj.NumChildren = reader.ReadUInt32();
            for (int i = 0; i < obj.NumChildren; i++)
                obj.Children.Add(reader.ReadUInt32());
            reader.BaseStream.Seek(23, SeekOrigin.Current);
            uint numStingers = reader.ReadUInt32();
            reader.BaseStream.Seek(24 * numStingers, SeekOrigin.Current);
            uint numRules = reader.ReadUInt32();
            for (uint i = 0; i < numRules; i++)
            {
                uint numSources = reader.ReadUInt32();
                reader.BaseStream.Seek(4 * numSources, SeekOrigin.Current);
                uint numDestinations = reader.ReadUInt32();
                reader.BaseStream.Seek(4 * numDestinations, SeekOrigin.Current);
                reader.BaseStream.Seek(bnkVersion <= 0x84 ? 45 : 47, SeekOrigin.Current);
                bool hasTransobject = reader.ReadByte() != 0;
                if (hasTransobject) reader.BaseStream.Seek(30, SeekOrigin.Current);
            }
            reader.BaseStream.Seek(1, SeekOrigin.Current);
            obj.NumArguments = reader.ReadUInt32();
            for (int i = 0; i < obj.NumArguments; i++)
            {
                var arg = new MusicSwitchArgument { GroupId = reader.ReadUInt32() };
                obj.Arguments.Add(arg);
            }
            for (int i = 0; i < obj.NumArguments; i++)
            {
                obj.Arguments[i].GroupType = reader.ReadByte();
            }
            uint treeSize = reader.ReadUInt32();
            reader.BaseStream.Seek(1, SeekOrigin.Current);
            obj.NumNodes = treeSize / 12;
            for (int i = 0; i < obj.NumNodes; i++)
            {
                var node = new MusicSwitchNode { Key = reader.ReadUInt32(), AudioId = reader.ReadUInt32() };
                reader.BaseStream.Seek(4, SeekOrigin.Current);
                obj.Nodes.Add(node);
            }
            obj.RawData = fullObjectBytes;
            musicSwitches.Add(obj);
            return true;
        }
    }
} 