using System;
using System.Collections.Generic;

namespace LoL_Announcer_Editor
{
    public class SoundStructure
    {
        public uint PluginID { get; set; }
        public ushort Type { get; set; }         // 2 bytes
        public ushort Company { get; set; }      // 2 bytes
        public byte StreamType { get; set; }     // 1 byte
        public int SourceID { get; set; }
        public int InMemoryMediaSize { get; set; }
        public byte SourceBits { get; set; }     // 1 byte
        public bool IsLanguageSpecific { get; set; }
        public bool Prefetch { get; set; }
        public bool NonCachable { get; set; }
        public bool HasSource { get; set; }
        public byte BIsOverrideParentFX { get; set; }     // 1 byte
        public byte NumFx { get; set; }                   // 1 byte
        public byte BIsOverrideParentMetadata { get; set; } // 1 byte
        public byte NumPluginMetadata { get; set; }       // 1 byte (added for completeness)
        public byte BOverrideAttachmentParams { get; set; } // 1 byte (added for completeness)
        public uint OverrideBusID { get; set; }           // 4 bytes (added for completeness)
        public uint DirectParentID { get; set; }          // 4 bytes (added for completeness)
    }

    public class Sound
    {
        public uint SelfId { get; set; }
        public uint Unknown1 { get; set; } // 4 bytes after SelfId
        public byte IsStreamed { get; set; }
        public byte[] Unknown2 { get; set; } // 3 bytes if bnkVersion <= 0x59
        public byte[] Unknown3 { get; set; } // 4 bytes if bnkVersion <= 0x70
        public uint FileId { get; set; }
        public byte[] RawData { get; set; } // All remaining bytes for this object
        // Announcer/voice fields (after basic Sound fields):
        public int Reserved0 { get; set; }
        public int StreamSourceType { get; set; }  // 0=Embedded, 1=Streamed, 2=Streamed(ZeroLatency)
        public uint StreamFileID { get; set; }
        public int StreamSourceID { get; set; }
        public int StreamFileOffset { get; set; }  // Only if embedded (StreamSourceType == 0)
        public int StreamFileLength { get; set; }  // Only if embedded (StreamSourceType == 0)
        public byte SoundType { get; set; }        // 0=SFX, 1=Voice
        public SoundStructure SoundStructure { get; set; }
    }

    public class MusicTrack
    {
        public uint SelfId { get; set; }
        public uint TrackCount { get; set; }
        public List<uint> FileIds { get; set; } = new List<uint>();
        public bool HasSwitchIds { get; set; }
        public uint SwitchGroupId { get; set; }
        public List<uint> SwitchIds { get; set; } = new List<uint>();
        public uint ParentId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class MusicContainer
    {
        public uint SelfId { get; set; }
        public uint MusicSwitchId { get; set; }
        public uint SoundObjectId { get; set; }
        public uint MusicTrackIdAmount { get; set; }
        public List<uint> MusicTrackIds { get; set; } = new List<uint>();
        public byte[] RawData { get; set; }
    }

    public class MusicSwitch
    {
        public uint SelfId { get; set; }
        public uint ParentId { get; set; }
        public uint NumChildren { get; set; }
        public List<uint> Children { get; set; } = new List<uint>();
        public uint NumArguments { get; set; }
        public List<MusicSwitchArgument> Arguments { get; set; } = new List<MusicSwitchArgument>();
        public uint NumNodes { get; set; }
        public List<MusicSwitchNode> Nodes { get; set; } = new List<MusicSwitchNode>();
        public byte[] RawData { get; set; }
    }

    public class MusicSwitchArgument
    {
        public uint GroupId { get; set; }
        public byte GroupType { get; set; }
    }

    public class MusicSwitchNode
    {
        public uint Key { get; set; }
        public uint AudioId { get; set; }
    }

    public class EventAction
    {
        public uint SelfId { get; set; }
        public byte Scope { get; set; }
        public byte Type { get; set; }
        public uint SoundObjectId { get; set; }
        public uint SwitchGroupId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class Event
    {
        public uint SelfId { get; set; }
        public byte EventAmount { get; set; }
        public List<uint> EventIds { get; set; } = new List<uint>();
        public byte[] RawData { get; set; }
    }

    public class PlaylistEntry
    {
        public uint PlayID { get; set; }      // Sound ID (4 bytes)
        public uint Weight { get; set; }      // Weight/probability value (4 bytes)
        public uint RsGroupID { get; set; }   // Random/Sequential group ID (4 bytes)
    }

    public class RandomContainer
    {
        public uint SelfId { get; set; }
        public uint SwitchContainerId { get; set; }
        public uint SoundIdAmount { get; set; }  // Kept for backward compatibility (= PlaylistLength)
        public List<uint> SoundIds { get; set; } = new List<uint>();  // Kept for backward compatibility (= PlayIDs from Playlist)
        public List<PlaylistEntry> Playlist { get; set; } = new List<PlaylistEntry>();  // Actual 8-byte structure (playID + weight)
        public byte[] RawData { get; set; }

        // New fields for proper parsing (matching Python parser)
        public uint ChildrenCount { get; set; }  // ulNumChilds (u32)
        public List<uint> ChildrenIds { get; set; } = new List<uint>();  // pChildren array
        public ushort PlaylistLength { get; set; }  // ulPlayListItem (u16)
    }

    public class SwitchContainer
    {
        public uint SelfId { get; set; }
        public uint ParentId { get; set; }
        public byte GroupType { get; set; }
        public uint GroupId { get; set; }
        public uint NumChildren { get; set; }
        public List<uint> Children { get; set; } = new List<uint>();
        public byte[] RawData { get; set; }
    }

    public class ActorMixer
    {
        public uint SelfId { get; set; }
        public uint ParentId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class Settings
    {
        public uint SelfId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class AudioBus
    {
        public uint SelfId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class BlendContainer
    {
        public uint SelfId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class Attenuation
    {
        public uint SelfId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class DialogueEvent
    {
        public uint SelfId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class MotionBus
    {
        public uint SelfId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class MotionFx
    {
        public uint SelfId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class Effect
    {
        public uint SelfId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class AuxiliaryBus
    {
        public uint SelfId { get; set; }
        public byte[] RawData { get; set; }
    }

    public class BnkObjectRecord
    {
        public byte Type { get; set; }
        public uint Length { get; set; }
        public byte[] RawData { get; set; } // Full object: type + length + data
        public object? ParsedObject { get; set; } // Sound, Event, etc., or null for unknown
        public bool IsChanged { get; set; } = false;
        public uint? ObjectId { get; set; } // For quick lookup (SelfId, etc.)
    }

    public class BnkFullFidelity
    {
        public List<BnkObjectRecord> AllObjectsInOrder { get; set; } = new List<BnkObjectRecord>();
        public uint BnkVersion { get; set; } = 0x86; // Default to 0x86, will be set during parsing
    }

    public class BNKSections
    {
        public List<Sound> Sounds { get; set; } = new List<Sound>();
        public List<EventAction> EventActions { get; set; } = new List<EventAction>();
        public List<Event> Events { get; set; } = new List<Event>();
        public List<RandomContainer> RandomContainers { get; set; } = new List<RandomContainer>();
        public List<SwitchContainer> SwitchContainers { get; set; } = new List<SwitchContainer>();
        public List<ActorMixer> ActorMixers { get; set; } = new List<ActorMixer>();
        public List<Settings> Settings { get; set; } = new List<Settings>();
        public List<AudioBus> AudioBuses { get; set; } = new List<AudioBus>();
        public List<BlendContainer> BlendContainers { get; set; } = new List<BlendContainer>();
        public List<Attenuation> Attenuations { get; set; } = new List<Attenuation>();
        public List<DialogueEvent> DialogueEvents { get; set; } = new List<DialogueEvent>();
        public List<MotionBus> MotionBuses { get; set; } = new List<MotionBus>();
        public List<MotionFx> MotionFxs { get; set; } = new List<MotionFx>();
        public List<Effect> Effects { get; set; } = new List<Effect>();
        public List<AuxiliaryBus> AuxiliaryBuses { get; set; } = new List<AuxiliaryBus>();
        public List<MusicContainer> MusicSegments { get; set; } = new List<MusicContainer>();
        public List<MusicTrack> MusicTracks { get; set; } = new List<MusicTrack>();
        public List<MusicSwitch> MusicSwitches { get; set; } = new List<MusicSwitch>();
        public List<MusicContainer> MusicPlaylists { get; set; } = new List<MusicContainer>();
    }
} 
