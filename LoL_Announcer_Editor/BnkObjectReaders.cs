using System;
using System.IO;
using System.Collections.Generic;

namespace LoL_Announcer_Editor
{
    /// <summary>
    /// Reads individual HIRC objects from a BinaryReader into structured objects.
    /// No raw byte arrays are stored; every field is parsed into named properties.
    /// </summary>
    public static class BnkObjectReaders
    {
        // ── Type 2: Sound ─────────────────────────────────────────────────────
        public static bool ReadSoundObject(BinaryReader reader, List<Sound> sounds, uint bnkVersion, byte[] _unused)
        {
            var obj = new Sound();
            try
            {
                obj.SelfId             = reader.ReadUInt32();
                obj.PluginId           = reader.ReadUInt32();
                obj.StreamType         = reader.ReadByte();
                obj.SourceId           = reader.ReadUInt32();
                obj.InMemoryMediaSize  = reader.ReadUInt32();
                obj.SourceBits         = reader.ReadByte();
                obj.NodeBaseParams     = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);
            }
            catch { }
            sounds.Add(obj);
            return true;
        }

        // ── Type 3: Event Action ──────────────────────────────────────────────
        public static bool ReadEventActionObject(BinaryReader reader, List<EventAction> eventActions, byte[] _unused)
        {
            var obj = new EventAction();
            try
            {
                obj.SelfId    = reader.ReadUInt32();
                obj.Scope     = reader.ReadByte();
                obj.ActionType = reader.ReadByte();
                obj.TargetId  = reader.ReadUInt32();
                // Additional parameter list (may be empty)
                obj.ParameterCount = reader.ReadByte();
                for (int i = 0; i < obj.ParameterCount; i++)
                    obj.ParameterTypes.Add(reader.ReadByte());
                for (int i = 0; i < obj.ParameterCount; i++)
                    obj.ParameterValues.Add(reader.ReadSingle());
            }
            catch { }
            eventActions.Add(obj);
            return true;
        }

        // ── Type 4: Event ─────────────────────────────────────────────────────
        public static bool ReadEventObject(BinaryReader reader, List<Event> events, uint bnkVersion, byte[] _unused)
        {
            var obj = new Event();
            try
            {
                obj.SelfId      = reader.ReadUInt32();
                obj.EventAmount = reader.ReadByte();
                for (int i = 0; i < obj.EventAmount; i++)
                    obj.EventIds.Add(reader.ReadUInt32());
            }
            catch { }
            events.Add(obj);
            return true;
        }

        // ── Type 5: Random / Sequence Container ───────────────────────────────
        public static bool ReadRandomContainerObject(BinaryReader reader, List<RandomContainer> randomContainers, uint bnkVersion, byte[] _unused)
        {
            var obj = new RandomContainer();
            try
            {
                obj.SelfId           = reader.ReadUInt32();
                obj.NodeBaseParams   = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);
                obj.LoopCount           = reader.ReadUInt16();
                obj.LoopModMin          = reader.ReadUInt16();
                obj.LoopModMax          = reader.ReadUInt16();
                obj.TransitionTime       = reader.ReadSingle();
                obj.TransitionTimeModMin = reader.ReadSingle();
                obj.TransitionTimeModMax = reader.ReadSingle();
                obj.TransitionMode = reader.ReadByte();
                obj.AvoidRepeatCount = reader.ReadUInt16();
                obj.RandomMode = reader.ReadByte();

                // BNK 145 (Wwise 2021) has an extra byte here before the child count
                if (bnkVersion >= 140) reader.ReadByte(); 
                obj.SoundIdAmount    = reader.ReadUInt32();
                for (int i = 0; i < obj.SoundIdAmount; i++)
                    obj.SoundIds.Add(reader.ReadUInt32());
                obj.PlaylistLength   = reader.ReadUInt16();
                for (int i = 0; i < obj.PlaylistLength; i++)
                {
                    var entry = new PlaylistEntry {
                        PlayID    = reader.ReadUInt32(),
                        Weight    = reader.ReadUInt32()
                    };
                    if (bnkVersion > 0x70) entry.RsGroupID = reader.ReadUInt32();
                    obj.Playlist.Add(entry);
                }
            }
            catch { }
            randomContainers.Add(obj);
            return true;
        }

        // ── Type 6: Switch Container ──────────────────────────────────────────
        public static bool ReadSwitchContainerObject(BinaryReader reader, List<SwitchContainer> switchContainers, uint bnkVersion, byte[] _unused)
        {
            var obj = new SwitchContainer();
            try
            {
                obj.SelfId                 = reader.ReadUInt32();
                obj.NodeBaseParams         = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);
                reader.BaseStream.Seek(1, SeekOrigin.Current); // loop flags byte
                obj.GroupType              = reader.ReadByte();
                obj.GroupId                = reader.ReadUInt32();
                obj.DefaultSwitchId        = reader.ReadUInt32();
                obj.IsContinuousValidation = reader.ReadByte() != 0;
                if (bnkVersion >= 140) reader.ReadByte(); // Extra flags byte
                uint childCount = reader.ReadUInt32();
                for (int i = 0; i < childCount; i++)
                    obj.Children.Add(reader.ReadUInt32());
                uint groupCount = reader.ReadUInt32();
                for (int i = 0; i < groupCount; i++)
                {
                    var group = new SwitchGroup { SwitchId = reader.ReadUInt32() };
                    uint itemCount = reader.ReadUInt32();
                    for (int j = 0; j < itemCount; j++)
                        group.Items.Add(reader.ReadUInt32());
                    obj.SwitchGroups.Add(group);
                }
            }
            catch { }
            switchContainers.Add(obj);
            return true;
        }

        // ── Type 7: Actor-Mixer ───────────────────────────────────────────────
        public static bool ReadActorMixerObject(BinaryReader reader, List<ActorMixer> actorMixers, uint bnkVersion, byte[] _unused)
        {
            var obj = new ActorMixer();
            try
            {
                obj.SelfId       = reader.ReadUInt32();
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);
                if (bnkVersion >= 140) reader.ReadByte(); // Extra flags byte
                uint childCount  = reader.ReadUInt32();
                for (int i = 0; i < childCount; i++)
                    obj.Children.Add(reader.ReadUInt32());
            }
            catch { }
            actorMixers.Add(obj);
            return true;
        }

        // ── Type 8: Audio Bus ─────────────────────────────────────────────────
        public static bool ReadAudioBusObject(BinaryReader reader, List<AudioBus> audioBuses, byte[] _unused)
        {
            var obj = new AudioBus();
            try
            {
                obj.SelfId      = reader.ReadUInt32();
                obj.ParentBusId = reader.ReadUInt32();
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, 0);
            }
            catch { }
            audioBuses.Add(obj);
            return true;
        }

        // ── Type 9: Blend Container ───────────────────────────────────────────
        public static bool ReadBlendContainerObject(BinaryReader reader, List<BlendContainer> blendContainers, uint bnkVersion, byte[] _unused)
        {
            var obj = new BlendContainer();
            try
            {
                obj.SelfId       = reader.ReadUInt32();
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);
                uint childCount  = reader.ReadUInt32();
                for (int i = 0; i < childCount; i++)
                    obj.Children.Add(reader.ReadUInt32());
            }
            catch { }
            blendContainers.Add(obj);
            return true;
        }

        // ── Type 10: Music Segment ────────────────────────────────────────────
        public static void ReadMusicContainerObject(BinaryReader reader, List<MusicSegment> containers, uint bnkVersion, byte[] _unused)
        {
            var obj = new MusicSegment();
            try
            {
                obj.SelfId     = reader.ReadUInt32();
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);
                uint count     = reader.ReadUInt32();
                for (int i = 0; i < count; i++) obj.MusicTrackIds.Add(reader.ReadUInt32());
            }
            catch { }
            containers.Add(obj);
        }

        // ── Type 11: Music Track ──────────────────────────────────────────────
        public static bool ReadMusicTrackObject(BinaryReader reader, List<MusicTrack> tracks, uint bnkVersion, byte[] _unused)
        {
            var obj = new MusicTrack();
            try
            {
                obj.SelfId = reader.ReadUInt32();
                uint count = reader.ReadUInt32();
                for (int i = 0; i < count; i++) obj.FileIds.Add(reader.ReadUInt32());
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);
            }
            catch { }
            tracks.Add(obj);
            return true;
        }

        // ── Type 12: Music Switch Container ──────────────────────────────────
        public static bool ReadMusicSwitchObject(BinaryReader reader, List<MusicSwitch> switches, uint bnkVersion, byte[] _unused)
        {
            var obj = new MusicSwitch();
            try
            {
                obj.SelfId     = reader.ReadUInt32();
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);
                uint count     = reader.ReadUInt32();
                for (int i = 0; i < count; i++) obj.Children.Add(reader.ReadUInt32());
            }
            catch { }
            switches.Add(obj);
            return true;
        }

        // ── Type 13: Music Playlist Container ────────────────────────────────
        public static void ReadMusicContainerObject(BinaryReader reader, List<MusicPlaylist> containers, uint bnkVersion, byte[] _unused)
        {
            var obj = new MusicPlaylist();
            try
            {
                obj.SelfId     = reader.ReadUInt32();
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);
                uint count     = reader.ReadUInt32();
                for (int i = 0; i < count; i++) obj.MusicTrackIds.Add(reader.ReadUInt32());
            }
            catch { }
            containers.Add(obj);
        }

        // ── Type 14: Attenuation ─────────────────────────────────────────────
        public static bool ReadAttenuationObject(BinaryReader reader, List<Attenuation> attenuations, byte[] _unused)
        {
            var obj = new Attenuation();
            try
            {
                obj.SelfId            = reader.ReadUInt32();
                obj.EnableHeightSpread = reader.ReadByte();
            }
            catch { }
            attenuations.Add(obj);
            return true;
        }

        // ── Type 15: Dialogue Event ──────────────────────────────────────────
        public static bool ReadDialogueEventObject(BinaryReader reader, List<DialogueEvent> dialogueEvents, byte[] _unused)
        {
            var obj = new DialogueEvent();
            try
            {
                obj.SelfId            = reader.ReadUInt32();
                obj.DecisionTreeDepth = reader.ReadUInt32();
            }
            catch { }
            dialogueEvents.Add(obj);
            return true;
        }

        // ── Type 16: Motion Bus ──────────────────────────────────────────────
        public static bool ReadMotionBusObject(BinaryReader reader, List<MotionBus> motionBuses, byte[] _unused)
        {
            var obj = new MotionBus();
            try
            {
                obj.SelfId   = reader.ReadUInt32();
                obj.ParentId = reader.ReadUInt32();
            }
            catch { }
            motionBuses.Add(obj);
            return true;
        }

        // ── Type 17: Motion FX ───────────────────────────────────────────────
        public static bool ReadMotionFxObject(BinaryReader reader, List<MotionFx> motionFxs, byte[] _unused)
        {
            var obj = new MotionFx();
            try
            {
                obj.SelfId           = reader.ReadUInt32();
                obj.MotionFxPluginId = reader.ReadUInt32();
            }
            catch { }
            motionFxs.Add(obj);
            return true;
        }

        // ── Type 18: Effect ───────────────────────────────────────────────────
        public static void ReadEffectObject(BinaryReader reader, List<Effect> effects, byte[] _unused)
        {
            var obj = new Effect();
            try
            {
                obj.SelfId     = reader.ReadUInt32();
                obj.PluginId   = reader.ReadUInt32();
                obj.IsShareSet = reader.ReadByte();
            }
            catch { }
            effects.Add(obj);
        }

        // ── Type 19: Auxiliary Bus ────────────────────────────────────────────
        public static void ReadAuxiliaryBusObject(BinaryReader reader, List<AuxiliaryBus> buses, byte[] _unused)
        {
            var obj = new AuxiliaryBus();
            try
            {
                obj.SelfId      = reader.ReadUInt32();
                obj.ParentBusId = reader.ReadUInt32();
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, 0);
            }
            catch { }
            buses.Add(obj);
        }

        // ── Type 1: Settings ─────────────────────────────────────────────────
        public static bool ReadSettingsObject(BinaryReader reader, List<Settings> settings, byte[] _unused)
        {
            var obj = new Settings();
            try
            {
                obj.SelfId      = reader.ReadUInt32();
                obj.NumSettings = reader.ReadByte();
                for (int i = 0; i < obj.NumSettings; i++) obj.SettingTypes.Add(reader.ReadByte());
                for (int i = 0; i < obj.NumSettings; i++) obj.SettingValues.Add(reader.ReadSingle());
            }
            catch { }
            settings.Add(obj);
            return true;
        }
    }
}
