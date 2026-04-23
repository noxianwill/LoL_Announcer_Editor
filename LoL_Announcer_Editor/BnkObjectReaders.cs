using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoL_Announcer_Editor
{
    public static class BnkObjectReaders
    {
        // ── Type 2: Sound ─────────────────────────────────────────────────────
        public static bool ReadSoundObject(BinaryReader reader, List<Sound> sounds, uint bnkVersion, byte[] _unused)
        {
            var obj = new Sound();
            try
            {
                obj.SelfId = reader.ReadUInt32();
                
                // 1. Source Data (CAkBankMgr::LoadSource)
                uint pluginId = reader.ReadUInt32();
                byte streamType = reader.ReadByte();
                obj.SourceId = reader.ReadUInt32(); // The actual WEM ID
                uint inMemorySize = reader.ReadUInt32();
                byte sourceBits = reader.ReadByte();
                
                // 2. Node Base Params
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);
            }
            catch (Exception ex)
            {
                WwiseExporterLogger.LogError($"    [Type 2] Sound {obj.SelfId} Parse Error: {ex.Message}");
            }
            sounds.Add(obj);
            return true;
        }

        // ── Type 3: Action ────────────────────────────────────────────────────
        public static bool ReadEventActionObject(BinaryReader reader, List<EventAction> actions, byte[] _unused)
        {
            var obj = new EventAction();
            try
            {
                obj.SelfId   = reader.ReadUInt32();
                obj.ActionType = reader.ReadUInt16(); // 2 bytes in BNK 62+
                obj.TargetId = reader.ReadUInt32();
                
                // Read idExt_4 (1 byte)
                reader.ReadByte(); 

                // Read AkPropBundle (float)
                byte cProps = reader.ReadByte();
                for (int i = 0; i < cProps; i++) reader.ReadByte(); // IDs
                for (int i = 0; i < cProps; i++) reader.ReadSingle(); // Values

                // Read AkPropBundle (int)
                cProps = reader.ReadByte();
                for (int i = 0; i < cProps; i++) reader.ReadByte(); // IDs
                for (int i = 0; i < cProps; i++) reader.ReadInt32(); // Values

                // If it's a Play action (0x0403), read PlayActionParams
                if (obj.ActionType == 0x0403)
                {
                    obj.FadeCurve = reader.ReadByte(); // byBitVector (bit 0-2 is eFadeCurve)
                    obj.BankId = reader.ReadUInt32();
                }

                WwiseExporterLogger.Log($"    [Type 3] Action {obj.SelfId}: Target {obj.TargetId}, Type {obj.ActionType}");
            }
            catch (Exception ex)
            {
                WwiseExporterLogger.LogError($"    [Type 3] Action {obj.SelfId} Parse Error: {ex.Message}");
            }
            actions.Add(obj);
            return true;
        }

        // ── Type 4: Event ─────────────────────────────────────────────────────
        public static bool ReadEventObject(BinaryReader reader, List<Event> events, uint bnkVersion, byte[] _unused)
        {
            var obj = new Event();
            try
            {
                obj.SelfId = reader.ReadUInt32();
                // In BNK 145, the action count is a single byte.
                uint count = reader.ReadByte();
                for (int i = 0; i < count; i++) 
                {
                    uint actionId = reader.ReadUInt32();
                    obj.EventIds.Add(actionId);
                }
                WwiseExporterLogger.Log($"    [Type 4] Event {obj.SelfId}: {obj.EventIds.Count} actions found.");
            }
            catch (Exception ex)
            {
                WwiseExporterLogger.LogError($"    [Type 4] Event {obj.SelfId} Parse Error: {ex.Message}");
            }
            events.Add(obj);
            return true;
        }

        // ── Type 5: Random Container ──────────────────────────────────────────
        public static bool ReadRandomContainerObject(BinaryReader reader, List<RandomContainer> randomContainers, uint bnkVersion, byte[] _unused)
        {
            var obj = new RandomContainer();
            try
            {
                obj.SelfId = reader.ReadUInt32();
                
                // 1. Read NodeBaseParams
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);

                // 2. Read Random Container specific fields (BNK 145)
                // In Wwise 2022.1 (145), this is a 24-byte block before children count.
                obj.LoopCount = reader.ReadUInt16();
                obj.LoopModMin = reader.ReadUInt16();
                obj.LoopModMax = reader.ReadUInt16();
                obj.TransitionTime = reader.ReadSingle();
                obj.TransitionTimeModMin = reader.ReadSingle();
                obj.TransitionTimeModMax = reader.ReadSingle();
                obj.AvoidRepeatCount = reader.ReadUInt16();
                obj.TransitionMode = reader.ReadByte();
                obj.RandomMode = reader.ReadByte();
                obj.Mode = reader.ReadByte();
                obj.byBitVector = reader.ReadByte();

                // 3. Read Children
                uint count = reader.ReadUInt32();
                obj.SoundIdAmount = count;
                for (int i = 0; i < (int)count; i++)
                {
                    uint childId = reader.ReadUInt32();
                    obj.SoundIds.Add(childId);
                }

                // 4. Read Playlist (AkPlaylistItem)
                uint playlistCount = reader.ReadUInt16();
                for (int i = 0; i < (int)playlistCount; i++)
                {
                    obj.Playlist.Add(new PlaylistItem {
                        Id = reader.ReadUInt32(),
                        Weight = reader.ReadInt32()
                    });
                }

                WwiseExporterLogger.Log($"    [Type 5] Container {obj.SelfId}: Found {obj.SoundIdAmount} children.");
            }
            catch (Exception ex) 
            {
                WwiseExporterLogger.LogError($"    ERROR in ReadRandomContainerObject {obj.SelfId}: {ex.Message}");
            }
            randomContainers.Add(obj);
            return true;
        }

        // ── Type 6: Switch Container ──────────────────────────────────────────
        public static bool ReadSwitchContainerObject(BinaryReader reader, List<SwitchContainer> switchContainers, uint bnkVersion, byte[] _unused)
        {
            var obj = new SwitchContainer();
            try
            {
                obj.SelfId = reader.ReadUInt32();
                
                // 1. Read NodeBaseParams
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);

                // 2. Read Switch Container specific fields (BNK 145)
                // In Wwise 2022.1 (145), this is a 10-byte block before children.
                reader.ReadByte();   // eGroupType
                reader.ReadUInt32(); // ulGroupID
                reader.ReadUInt32(); // ulDefaultSwitchID
                reader.ReadByte();   // bIsContinuousValidation

                // 3. Read Children
                uint count = reader.ReadUInt32();
                for (int i = 0; i < (int)count; i++)
                {
                    obj.Children.Add(reader.ReadUInt32());
                }

                WwiseExporterLogger.Log($"    [Type 6] SwitchContainer {obj.SelfId}: Found {obj.Children.Count} children.");
            }
            catch (Exception ex)
            {
                WwiseExporterLogger.LogError($"    ERROR in ReadSwitchContainerObject {obj.SelfId}: {ex.Message}");
            }
            switchContainers.Add(obj);
            return true;
        }

        // ── Type 7: Actor-Mixer ───────────────────────────────────────────────
        public static bool ReadActorMixerObject(BinaryReader reader, List<ActorMixer> actorMixers, uint bnkVersion, byte[] _unused)
        {
            var obj = new ActorMixer();
            try
            {
                obj.SelfId = reader.ReadUInt32();
                
                // In BNK 145, Actor-Mixer (Type 7) follows NodeBaseParams with a children count.
                obj.NodeBaseParams = BnkBinaryHelpers.ReadNodeBaseParams(reader, bnkVersion);

                uint count = reader.ReadUInt32();
                for (int i = 0; i < (int)count; i++)
                {
                    obj.Children.Add(reader.ReadUInt32());
                }

                WwiseExporterLogger.Log($"    [Type 7] ActorMixer {obj.SelfId}: Found {obj.Children.Count} children.");
            }
            catch (Exception ex)
            {
                WwiseExporterLogger.Log($"    ERROR in ReadActorMixerObject {obj.SelfId}: {ex.Message}");
            }
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
        // ── Type 1: Settings ──────────────────────────────────────────────────
        public static bool ReadSettingsObject(BinaryReader reader, List<Settings> settings, byte[] _unused)
        {
            var obj = new Settings();
            try { obj.SelfId = reader.ReadUInt32(); } catch { }
            settings.Add(obj);
            return true;
        }

        // ── Type 12: Music Switch ─────────────────────────────────────────────
        public static bool ReadMusicSwitchObject(BinaryReader reader, List<MusicSwitch> switches, uint bnkVersion, byte[] _unused)
        {
            var obj = new MusicSwitch();
            try { obj.SelfId = reader.ReadUInt32(); } catch { }
            switches.Add(obj);
            return true;
        }

        // ── Type 13: Music Playlist ───────────────────────────────────────────
        public static void ReadMusicContainerObject(BinaryReader reader, List<MusicPlaylist> playlists, uint bnkVersion, byte[] _unused)
        {
            var obj = new MusicPlaylist();
            try { obj.SelfId = reader.ReadUInt32(); } catch { }
            playlists.Add(obj);
        }

        public static bool ReadMusicPlaylistObject(BinaryReader reader, List<MusicPlaylist> playlists, byte[] _unused)
        {
            var obj = new MusicPlaylist();
            try { obj.SelfId = reader.ReadUInt32(); } catch { }
            playlists.Add(obj);
            return true;
        }

        // ── Type 14: Attenuation ──────────────────────────────────────────────
        public static bool ReadAttenuationObject(BinaryReader reader, List<Attenuation> attenuations, byte[] _unused)
        {
            var obj = new Attenuation();
            try { obj.SelfId = reader.ReadUInt32(); } catch { }
            attenuations.Add(obj);
            return true;
        }

        // ── Type 15: Dialogue Event ───────────────────────────────────────────
        public static bool ReadDialogueEventObject(BinaryReader reader, List<DialogueEvent> events, byte[] _unused)
        {
            var obj = new DialogueEvent();
            try { obj.SelfId = reader.ReadUInt32(); } catch { }
            events.Add(obj);
            return true;
        }

        // ── Type 16: Motion Bus ───────────────────────────────────────────────
        public static bool ReadMotionBusObject(BinaryReader reader, List<MotionBus> buses, byte[] _unused)
        {
            var obj = new MotionBus();
            try { obj.SelfId = reader.ReadUInt32(); } catch { }
            buses.Add(obj);
            return true;
        }

        // ── Type 17: Motion FX ────────────────────────────────────────────────
        public static bool ReadMotionFxObject(BinaryReader reader, List<MotionFx> fxs, byte[] _unused)
        {
            var obj = new MotionFx();
            try { obj.SelfId = reader.ReadUInt32(); } catch { }
            fxs.Add(obj);
            return true;
        }

        // ── Type 18: Effect ───────────────────────────────────────────────────
        public static bool ReadEffectObject(BinaryReader reader, List<Effect> effects, byte[] _unused)
        {
            var obj = new Effect();
            try { obj.SelfId = reader.ReadUInt32(); } catch { }
            effects.Add(obj);
            return true;
        }

        // ── Type 19: Auxiliary Bus ────────────────────────────────────────────
        public static bool ReadAuxiliaryBusObject(BinaryReader reader, List<AuxiliaryBus> buses, byte[] _unused)
        {
            var obj = new AuxiliaryBus();
            try { obj.SelfId = reader.ReadUInt32(); } catch { }
            buses.Add(obj);
            return true;
        }
    }
}
