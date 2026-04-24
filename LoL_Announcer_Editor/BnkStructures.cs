using System;
using System.Collections.Generic;
using System.IO;

namespace LoL_Announcer_Editor
{
    public class BNKSections
    {
        public uint BnkVersion { get; set; }
        /// <summary>BNK filename without extension, e.g. "announcer_global_female1_vo_events"</summary>
        public string SourceFileName { get; set; } = "";
        public List<Settings> Settings { get; set; } = new List<Settings>();
        public List<Sound> Sounds { get; set; } = new List<Sound>();
        public List<EventAction> EventActions { get; set; } = new List<EventAction>();
        public List<Event> Events { get; set; } = new List<Event>();
        public List<RandomContainer> RandomContainers { get; set; } = new List<RandomContainer>();
        public List<SwitchContainer> SwitchContainers { get; set; } = new List<SwitchContainer>();
        public List<ActorMixer> ActorMixers { get; set; } = new List<ActorMixer>();
        public List<AudioBus> AudioBuses { get; set; } = new List<AudioBus>();
        public List<BlendContainer> BlendContainers { get; set; } = new List<BlendContainer>();
        public List<MusicSegment> MusicSegments { get; set; } = new List<MusicSegment>();
        public List<MusicTrack> MusicTracks { get; set; } = new List<MusicTrack>();
        public List<MusicSwitch> MusicSwitches { get; set; } = new List<MusicSwitch>();
        public List<MusicPlaylist> MusicPlaylists { get; set; } = new List<MusicPlaylist>();
        public List<Attenuation> Attenuations { get; set; } = new List<Attenuation>();
        public List<DialogueEvent> DialogueEvents { get; set; } = new List<DialogueEvent>();
        public List<MotionBus> MotionBuses { get; set; } = new List<MotionBus>();
        public List<MotionFx> MotionFxs { get; set; } = new List<MotionFx>();
        public List<Effect> Effects { get; set; } = new List<Effect>();
        public List<AuxiliaryBus> AuxiliaryBuses { get; set; } = new List<AuxiliaryBus>();
    }

    /// <summary>
    /// One HIRC object record. RawData is only kept for object types we do not
    /// fully serialize ourselves (truly unknown type IDs). For all known types
    /// the writer uses the ParsedObject exclusively.
    /// </summary>
    public class BnkObjectRecord
    {
        public byte Type { get; set; }
        public uint Length { get; set; }
        public uint? ObjectId { get; set; }
        public object ParsedObject { get; set; }
        /// <summary>
        /// Kept ONLY for unknown/unhandled type IDs so the writer can round-trip
        /// them unchanged. For all known types this is null after parsing.
        /// </summary>
        public byte[] RawData { get; set; }
        public bool IsChanged { get; set; }
    }

    public class BnkFullFidelity
    {
        public uint BnkVersion { get; set; }
        public List<BnkObjectRecord> AllObjectsInOrder { get; set; } = new List<BnkObjectRecord>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Shared sub-structures
    // ──────────────────────────────────────────────────────────────────────────

    public class PropValue
    {
        public byte ID { get; set; }
        public float Value { get; set; }
    }

    public class PropValueInt
    {
        public byte ID { get; set; }
        public int Value { get; set; }
    }

    public class FxInfo
    {
        public byte Index { get; set; }
        public uint Id { get; set; }
        public byte IsShareSet { get; set; }
        public byte IsRendered { get; set; }
    }

    public class MetadataEntry
    {
        public uint Id { get; set; }
        public byte[] Payload { get; set; }
    }

    public class StateInstance
    {
        public uint StateId { get; set; }
        public uint InstanceId { get; set; }
    }

    public class StateGroup
    {
        public uint Id { get; set; }
        public byte SyncType { get; set; }
        public List<StateInstance> States { get; set; } = new List<StateInstance>();
    }

    public class RtpcPoint
    {
        public float X { get; set; }
        public float Y { get; set; }
        public uint Shape { get; set; }
    }

    /// <summary>
    /// One RTPC binding. Field names match the Wwise SDK / wwiser naming.
    /// Layout: ControlId(4) ParamId(1) CurveId(4) Scaling(1) PointCount(2) Points[PointCount]
    /// </summary>
    public class RtpcEntry
    {
        public uint ControlId { get; set; }        // RTPCID
        public byte ParamId { get; set; }          // RTPC_paramID (volume/pitch/…)
        public uint CurveId { get; set; }          // RtpcCurveID
        public byte Scaling { get; set; }          // eScaling
        public List<RtpcPoint> Points { get; set; } = new List<RtpcPoint>();
    }

    /// <summary>
    /// Common properties shared by every Wwise sound node.
    /// All fields are named after their Wwise SDK counterparts.
    /// </summary>
    public class NodeBaseParams
    {
        public byte OverrideParentFX { get; set; }
        public byte NumFx { get; set; }
        public byte FXBypassBits { get; set; }
        public List<FxInfo> FxInfos { get; set; } = new List<FxInfo>();

        public byte OverrideParentMetadata { get; set; }
        public byte NumMetadata { get; set; }
        public List<MetadataEntry> Metadata { get; set; } = new List<MetadataEntry>();

        public byte OverrideAttachmentParams { get; set; }
        public uint OutputBusId { get; set; }       // OverrideBusID
        public uint DirectParentId { get; set; }    // DirectParentID
        public byte OverrideParentPriority { get; set; }
        public byte OffsetPriority { get; set; }

        public List<PropValue> Props { get; set; } = new List<PropValue>();
        public List<PropValueInt> PropsInt { get; set; } = new List<PropValueInt>();

        public byte PositioningBits { get; set; }
        public byte uBits3d { get; set; }
        public byte bIsPannerEnabled { get; set; }

        public byte AuxBits { get; set; }
        public uint AuxBus0 { get; set; }
        public uint AuxBus1 { get; set; }
        public uint AuxBus2 { get; set; }
        public uint AuxBus3 { get; set; }
        public uint ReflectionsAuxBus { get; set; }

        // Advanced Settings (BNK 140+)
        public byte AdvBits { get; set; }
        public byte VirtualQueueBehavior { get; set; }
        public ushort MaxNumInstance { get; set; }
        public byte BelowThresholdBehavior { get; set; }
        public byte HdrBits { get; set; }

        // State properties
        public List<byte[]> StateProps { get; set; } = new List<byte[]>(); // 3 bytes each
        public List<StateGroup> StateGroups { get; set; } = new List<StateGroup>();

        // RTPCs
        public List<RtpcEntry> Rtpcs { get; set; } = new List<RtpcEntry>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // HIRC object types
    // ──────────────────────────────────────────────────────────────────────────

    public class Settings
    {
        public uint SelfId { get; set; }
        public byte NumSettings { get; set; }
        public List<byte> SettingTypes { get; set; } = new List<byte>();
        public List<float> SettingValues { get; set; } = new List<float>();
    }

    /// <summary>Type 2 — SFX / Voice sound object.</summary>
    public class Sound
    {
        public uint SelfId { get; set; }
        public uint PluginId { get; set; }       // Audio plug-in ID (e.g. Wwise Vorbis = 0x00040001)
        public byte StreamType { get; set; }     // 0=Embedded, 1=Streamed, 2=Prefetched
        public uint SourceId { get; set; }       // WEM file ID (media source ID)
        public uint InMemoryMediaSize { get; set; }
        public byte SourceBits { get; set; }
        public NodeBaseParams NodeBaseParams { get; set; } = new NodeBaseParams();
    }

    /// <summary>Type 3 — Event Action.</summary>
    public class EventAction
    {
        public uint SelfId { get; set; }
        public ushort ActionType { get; set; }
        public uint TargetId { get; set; }
        public uint SoundObjectId => TargetId;
        public byte FadeCurve { get; set; }
        public uint BankId { get; set; }
        // Compatibility fields for JSON export/debug view
        public byte Scope { get; set; }
        public uint SwitchGroupId { get; set; }
        public List<byte> ParameterTypes { get; set; } = new List<byte>();
        public List<float> ParameterValues { get; set; } = new List<float>();
    }

    /// <summary>Type 4 — Event.</summary>
    public class Event
    {
        public uint SelfId { get; set; }
        public byte EventAmount { get; set; }
        public List<uint> EventIds { get; set; } = new List<uint>();
    }

    public class PlaylistItem
    {
        public uint Id { get; set; }
        public int Weight { get; set; }
    }

    /// <summary>Type 5 — Random / Sequence Container.</summary>
    public class RandomContainer
    {
        public uint SelfId { get; set; }
        public NodeBaseParams NodeBaseParams { get; set; } = new NodeBaseParams();
        public ushort LoopCount { get; set; }
        public ushort LoopModMin { get; set; }
        public ushort LoopModMax { get; set; }
        public float TransitionTime { get; set; }
        public float TransitionTimeModMin { get; set; }
        public float TransitionTimeModMax { get; set; }
        public byte TransitionMode { get; set; }
        public ushort AvoidRepeatCount { get; set; }
        public byte RandomMode { get; set; }
        public byte Mode { get; set; }
        public byte byBitVector { get; set; }
        public uint SoundIdAmount { get; set; }
        public List<uint> SoundIds { get; set; } = new List<uint>();
        public List<uint> ChildrenIds => SoundIds;
        public ushort PlaylistLength { get; set; }
        public List<PlaylistItem> Playlist { get; set; } = new List<PlaylistItem>();
    }

    public class SwitchGroup
    {
        public uint SwitchId { get; set; }
        public List<uint> Items { get; set; } = new List<uint>();
    }

    /// <summary>Type 6 — Switch Container.</summary>
    public class SwitchContainer
    {
        public uint SelfId { get; set; }
        public NodeBaseParams NodeBaseParams { get; set; } = new NodeBaseParams();
        public byte GroupType { get; set; }
        public uint GroupId { get; set; }
        public uint DefaultSwitchId { get; set; }
        public bool IsContinuousValidation { get; set; }
        public List<uint> Children { get; set; } = new List<uint>();
        public List<SwitchGroup> SwitchGroups { get; set; } = new List<SwitchGroup>();
    }

    /// <summary>Type 7 — Actor-Mixer.</summary>
    public class ActorMixer
    {
        public uint SelfId { get; set; }
        public NodeBaseParams NodeBaseParams { get; set; } = new NodeBaseParams();
        public List<uint> Children { get; set; } = new List<uint>();
    }

    /// <summary>Type 8 — Audio Bus.</summary>
    public class AudioBus
    {
        public uint SelfId { get; set; }
        public uint ParentBusId { get; set; }
        public NodeBaseParams NodeBaseParams { get; set; } = new NodeBaseParams();
    }

    /// <summary>Type 9 — Blend Container.</summary>
    public class BlendContainer
    {
        public uint SelfId { get; set; }
        public NodeBaseParams NodeBaseParams { get; set; } = new NodeBaseParams();
        public List<uint> Children { get; set; } = new List<uint>();
    }

    /// <summary>Type 10 — Music Segment.</summary>
    public class MusicSegment
    {
        public uint SelfId { get; set; }
        public NodeBaseParams NodeBaseParams { get; set; } = new NodeBaseParams();
        public List<uint> MusicTrackIds { get; set; } = new List<uint>();
    }

    /// <summary>Type 11 — Music Track.</summary>
    public class MusicTrack
    {
        public uint SelfId { get; set; }
        public List<uint> FileIds { get; set; } = new List<uint>();
        public NodeBaseParams NodeBaseParams { get; set; } = new NodeBaseParams();
    }

    /// <summary>Type 12 — Music Switch Container.</summary>
    public class MusicSwitch
    {
        public uint SelfId { get; set; }
        public NodeBaseParams NodeBaseParams { get; set; } = new NodeBaseParams();
        public List<uint> Children { get; set; } = new List<uint>();
    }

    /// <summary>Type 13 — Music Playlist Container.</summary>
    public class MusicPlaylist
    {
        public uint SelfId { get; set; }
        public NodeBaseParams NodeBaseParams { get; set; } = new NodeBaseParams();
        public List<uint> MusicTrackIds { get; set; } = new List<uint>();
    }

    /// <summary>Type 14 — Attenuation.</summary>
    public class Attenuation
    {
        public uint SelfId { get; set; }
        public byte EnableHeightSpread { get; set; }
    }

    /// <summary>Type 15 — Dialogue Event.</summary>
    public class DialogueEvent
    {
        public uint SelfId { get; set; }
        public uint DecisionTreeDepth { get; set; }
    }

    /// <summary>Type 16 — Motion Bus.</summary>
    public class MotionBus
    {
        public uint SelfId { get; set; }
        public uint ParentId { get; set; }
    }

    /// <summary>Type 17 — Motion FX.</summary>
    public class MotionFx
    {
        public uint SelfId { get; set; }
        public uint MotionFxPluginId { get; set; }
    }

    /// <summary>Type 18 — Effect (FX).</summary>
    public class Effect
    {
        public uint SelfId { get; set; }
        public uint PluginId { get; set; }
        public byte IsShareSet { get; set; }
    }

    /// <summary>Type 19 — Auxiliary Bus.</summary>
    public class AuxiliaryBus
    {
        public uint SelfId { get; set; }
        public uint ParentBusId { get; set; }
        public NodeBaseParams NodeBaseParams { get; set; } = new NodeBaseParams();
    }
}
