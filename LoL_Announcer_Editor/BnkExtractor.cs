using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace LoL_Announcer_Editor
{
    public static class BnkExtractor
    {
        public static bool ParseEventBnkFile(string path, BNKSections sections)
        {
            if (!File.Exists(path)) { WemAdditionLogger.LogError($"File not found: {path}"); return false; }
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    var magic = reader.ReadBytes(4);
                    if (Encoding.ASCII.GetString(magic) != "BKHD") { WemAdditionLogger.LogError($"Invalid BKHD magic: {Encoding.ASCII.GetString(magic)}"); return false; }
                    uint bkhdLength = reader.ReadUInt32();
                    long bkhdDataStart = reader.BaseStream.Position;
                    uint bnkVersion = reader.ReadUInt32();
                    sections.BnkVersion = bnkVersion;
                    
                    // Skip the rest of BKHD
                    reader.BaseStream.Seek(bkhdDataStart + bkhdLength, SeekOrigin.Begin);
                    
                    uint sectionLength = SkipToSection(reader, "HIRC", true);
                    if (sectionLength == 0) { WemAdditionLogger.LogError("HIRC section length is 0 or not found"); return false; }
                    long initialPosition = reader.BaseStream.Position;
                    uint numOfObjects = reader.ReadUInt32();
                    uint objectsRead = 0;
                    while (reader.BaseStream.Position < initialPosition + sectionLength)
                    {
                        long objectStart = reader.BaseStream.Position;
                        byte type = reader.ReadByte();
                        uint objectLength = reader.ReadUInt32();
                        byte[] objectBytes = reader.ReadBytes((int)objectLength);
                        byte[] fullObjectBytes = new byte[objectLength + 5];
                        fullObjectBytes[0] = type;
                        Array.Copy(BitConverter.GetBytes(objectLength), 0, fullObjectBytes, 1, 4);
                        Array.Copy(objectBytes, 0, fullObjectBytes, 5, objectLength);
                        using (var ms = new MemoryStream(objectBytes))
                        using (var objReader = new BinaryReader(ms))
                        {
                            switch (type)
                            {
                                case 1: BnkObjectReaders.ReadSettingsObject(objReader, sections.Settings, fullObjectBytes); break;
                                case 2: BnkObjectReaders.ReadSoundObject(objReader, sections.Sounds, bnkVersion, fullObjectBytes); break;
                                case 3: BnkObjectReaders.ReadEventActionObject(objReader, sections.EventActions, fullObjectBytes); break;
                                case 4: BnkObjectReaders.ReadEventObject(objReader, sections.Events, bnkVersion, fullObjectBytes); break;
                                case 5: BnkObjectReaders.ReadRandomContainerObject(objReader, sections.RandomContainers, bnkVersion, fullObjectBytes); break;
                                case 6: BnkObjectReaders.ReadSwitchContainerObject(objReader, sections.SwitchContainers, bnkVersion, fullObjectBytes); break;
                                case 7: BnkObjectReaders.ReadActorMixerObject(objReader, sections.ActorMixers, bnkVersion, fullObjectBytes); break;
                                case 8: BnkObjectReaders.ReadAudioBusObject(objReader, sections.AudioBuses, fullObjectBytes); break;
                                case 9: BnkObjectReaders.ReadBlendContainerObject(objReader, sections.BlendContainers, bnkVersion, fullObjectBytes); break;
                                case 14: BnkObjectReaders.ReadAttenuationObject(objReader, sections.Attenuations, fullObjectBytes); break;
                                case 15: BnkObjectReaders.ReadDialogueEventObject(objReader, sections.DialogueEvents, fullObjectBytes); break;
                                case 16: BnkObjectReaders.ReadMotionBusObject(objReader, sections.MotionBuses, fullObjectBytes); break;
                                case 17: BnkObjectReaders.ReadMotionFxObject(objReader, sections.MotionFxs, fullObjectBytes); break;
                                case 18: BnkObjectReaders.ReadEffectObject(objReader, sections.Effects, fullObjectBytes); break;
                                case 19: BnkObjectReaders.ReadAuxiliaryBusObject(objReader, sections.AuxiliaryBuses, fullObjectBytes); break;
                                case 10: BnkObjectReaders.ReadMusicContainerObject(objReader, sections.MusicSegments, bnkVersion, fullObjectBytes); break;
                                case 11: BnkObjectReaders.ReadMusicTrackObject(objReader, sections.MusicTracks, bnkVersion, fullObjectBytes); break;
                                case 12: BnkObjectReaders.ReadMusicSwitchObject(objReader, sections.MusicSwitches, bnkVersion, fullObjectBytes); break;
                                case 13: BnkObjectReaders.ReadMusicContainerObject(objReader, sections.MusicPlaylists, bnkVersion, fullObjectBytes); break;
                            }
                        }
                        objectsRead++;
                    }
                }
                return true;
            }
            catch (Exception ex) 
            { 
                WemAdditionLogger.LogException("ParseEventBnkFile", ex);
                return false; 
            }
        }

        public static bool ParseEventBnkFile_FullFidelity(string path, BnkFullFidelity fullFidelity, BNKSections sections)
        {
            if (!File.Exists(path)) { WemAdditionLogger.LogError($"File not found: {path}"); return false; }
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    var magic = reader.ReadBytes(4);
                    if (Encoding.ASCII.GetString(magic) != "BKHD") { WemAdditionLogger.LogError($"Invalid BKHD magic: {Encoding.ASCII.GetString(magic)}"); return false; }
                    uint bkhdLength = reader.ReadUInt32();
                    long bkhdDataStart = reader.BaseStream.Position;
                    uint bnkVersion = reader.ReadUInt32();
                    fullFidelity.BnkVersion = bnkVersion;
                    sections.BnkVersion = bnkVersion;
                    
                    // Skip the rest of BKHD
                    reader.BaseStream.Seek(bkhdDataStart + bkhdLength, SeekOrigin.Begin);

                    uint sectionLen = SkipToSection(reader, "HIRC", true);
                    if (sectionLen == 0) { WemAdditionLogger.LogError("HIRC section length is 0 or not found"); return false; }
                    byte[] hircData = reader.ReadBytes((int)sectionLen);
                    using (var ms = new MemoryStream(hircData))
                    using (var hircReader = new BinaryReader(ms))
                    {
                        uint numObjects = hircReader.ReadUInt32();
                        for (int i = 0; i < numObjects; i++)
                        {
                            byte type = hircReader.ReadByte();
                            uint len = hircReader.ReadUInt32();
                            WwiseExporterLogger.Log($"    HIRC Object: Type {type}, Len {len}");
                            byte[] objPayload = hircReader.ReadBytes((int)len);
                            byte[] fullObj = new byte[len + 5];
                            fullObj[0] = type;
                            Array.Copy(BitConverter.GetBytes(len), 0, fullObj, 1, 4);
                            Array.Copy(objPayload, 0, fullObj, 5, len);

                            // RawData only stored for unknown type IDs (pass-through). Known types are fully parsed.
                            var record = new BnkObjectRecord { Type = type, Length = len, RawData = null };
                            using (var objMs = new MemoryStream(objPayload))
                            using (var objReader = new BinaryReader(objMs))
                            {
                                switch (type)
                                {
                                    case 1:
                                        var settings = new List<Settings>();
                                        BnkObjectReaders.ReadSettingsObject(objReader, settings, null);
                                        record.ParsedObject = settings.FirstOrDefault();
                                        record.ObjectId = settings.FirstOrDefault()?.SelfId;
                                        sections.Settings.AddRange(settings);
                                        break;
                                    case 2:
                                        var sounds = new List<Sound>();
                                        BnkObjectReaders.ReadSoundObject(objReader, sounds, bnkVersion, null);
                                        record.ParsedObject = sounds.FirstOrDefault();
                                        record.ObjectId = sounds.FirstOrDefault()?.SelfId;
                                        sections.Sounds.AddRange(sounds);
                                        break;
                                    case 3:
                                        var actions = new List<EventAction>();
                                        BnkObjectReaders.ReadEventActionObject(objReader, actions, null);
                                        record.ParsedObject = actions.FirstOrDefault();
                                        record.ObjectId = actions.FirstOrDefault()?.SelfId;
                                        sections.EventActions.AddRange(actions);
                                        break;
                                    case 4:
                                        var events = new List<Event>();
                                        BnkObjectReaders.ReadEventObject(objReader, events, bnkVersion, null);
                                        record.ParsedObject = events.FirstOrDefault();
                                        record.ObjectId = events.FirstOrDefault()?.SelfId;
                                        sections.Events.AddRange(events);
                                        break;
                                    case 5:
                                        var containers = new List<RandomContainer>();
                                        BnkObjectReaders.ReadRandomContainerObject(objReader, containers, bnkVersion, null);
                                        record.ParsedObject = containers.FirstOrDefault();
                                        record.ObjectId = containers.FirstOrDefault()?.SelfId;
                                        sections.RandomContainers.AddRange(containers);
                                        break;
                                    case 6:
                                        var swContainers = new List<SwitchContainer>();
                                        BnkObjectReaders.ReadSwitchContainerObject(objReader, swContainers, bnkVersion, null);
                                        record.ParsedObject = swContainers.FirstOrDefault();
                                        record.ObjectId = swContainers.FirstOrDefault()?.SelfId;
                                        sections.SwitchContainers.AddRange(swContainers);
                                        break;
                                    case 7:
                                        var mixers = new List<ActorMixer>();
                                        BnkObjectReaders.ReadActorMixerObject(objReader, mixers, bnkVersion, null);
                                        record.ParsedObject = mixers.FirstOrDefault();
                                        record.ObjectId = mixers.FirstOrDefault()?.SelfId;
                                        sections.ActorMixers.AddRange(mixers);
                                        break;
                                    case 8:
                                        var audioBuses = new List<AudioBus>();
                                        BnkObjectReaders.ReadAudioBusObject(objReader, audioBuses, null);
                                        record.ParsedObject = audioBuses.FirstOrDefault();
                                        record.ObjectId = audioBuses.FirstOrDefault()?.SelfId;
                                        sections.AudioBuses.AddRange(audioBuses);
                                        break;
                                    case 9:
                                        var blendContainers = new List<BlendContainer>();
                                        BnkObjectReaders.ReadBlendContainerObject(objReader, blendContainers, bnkVersion, null);
                                        record.ParsedObject = blendContainers.FirstOrDefault();
                                        record.ObjectId = blendContainers.FirstOrDefault()?.SelfId;
                                        sections.BlendContainers.AddRange(blendContainers);
                                        break;
                                    case 10:
                                        var musicSegs = new List<MusicSegment>();
                                        BnkObjectReaders.ReadMusicContainerObject(objReader, musicSegs, bnkVersion, null);
                                        record.ParsedObject = musicSegs.FirstOrDefault();
                                        record.ObjectId = musicSegs.FirstOrDefault()?.SelfId;
                                        sections.MusicSegments.AddRange(musicSegs);
                                        break;
                                    case 11:
                                        var musicTracks = new List<MusicTrack>();
                                        BnkObjectReaders.ReadMusicTrackObject(objReader, musicTracks, bnkVersion, null);
                                        record.ParsedObject = musicTracks.FirstOrDefault();
                                        record.ObjectId = musicTracks.FirstOrDefault()?.SelfId;
                                        sections.MusicTracks.AddRange(musicTracks);
                                        break;
                                    case 12:
                                        var musicSwitches = new List<MusicSwitch>();
                                        BnkObjectReaders.ReadMusicSwitchObject(objReader, musicSwitches, bnkVersion, null);
                                        record.ParsedObject = musicSwitches.FirstOrDefault();
                                        record.ObjectId = musicSwitches.FirstOrDefault()?.SelfId;
                                        sections.MusicSwitches.AddRange(musicSwitches);
                                        break;
                                    case 13:
                                        var musicPlaylists = new List<MusicPlaylist>();
                                        BnkObjectReaders.ReadMusicContainerObject(objReader, musicPlaylists, bnkVersion, null);
                                        record.ParsedObject = musicPlaylists.FirstOrDefault();
                                        record.ObjectId = musicPlaylists.FirstOrDefault()?.SelfId;
                                        sections.MusicPlaylists.AddRange(musicPlaylists);
                                        break;
                                    case 14:
                                        var attenuations = new List<Attenuation>();
                                        BnkObjectReaders.ReadAttenuationObject(objReader, attenuations, null);
                                        record.ParsedObject = attenuations.FirstOrDefault();
                                        record.ObjectId = attenuations.FirstOrDefault()?.SelfId;
                                        sections.Attenuations.AddRange(attenuations);
                                        break;
                                    case 15:
                                        var dialogueEvents = new List<DialogueEvent>();
                                        BnkObjectReaders.ReadDialogueEventObject(objReader, dialogueEvents, null);
                                        record.ParsedObject = dialogueEvents.FirstOrDefault();
                                        record.ObjectId = dialogueEvents.FirstOrDefault()?.SelfId;
                                        sections.DialogueEvents.AddRange(dialogueEvents);
                                        break;
                                    case 16:
                                        var motionBuses = new List<MotionBus>();
                                        BnkObjectReaders.ReadMotionBusObject(objReader, motionBuses, null);
                                        record.ParsedObject = motionBuses.FirstOrDefault();
                                        record.ObjectId = motionBuses.FirstOrDefault()?.SelfId;
                                        sections.MotionBuses.AddRange(motionBuses);
                                        break;
                                    case 17:
                                        var motionFxs = new List<MotionFx>();
                                        BnkObjectReaders.ReadMotionFxObject(objReader, motionFxs, null);
                                        record.ParsedObject = motionFxs.FirstOrDefault();
                                        record.ObjectId = motionFxs.FirstOrDefault()?.SelfId;
                                        sections.MotionFxs.AddRange(motionFxs);
                                        break;
                                    case 18:
                                        var effects = new List<Effect>();
                                        BnkObjectReaders.ReadEffectObject(objReader, effects, null);
                                        record.ParsedObject = effects.FirstOrDefault();
                                        record.ObjectId = effects.FirstOrDefault()?.SelfId;
                                        sections.Effects.AddRange(effects);
                                        break;
                                    case 19:
                                        var auxBuses = new List<AuxiliaryBus>();
                                        BnkObjectReaders.ReadAuxiliaryBusObject(objReader, auxBuses, null);
                                        record.ParsedObject = auxBuses.FirstOrDefault();
                                        record.ObjectId = auxBuses.FirstOrDefault()?.SelfId;
                                        sections.AuxiliaryBuses.AddRange(auxBuses);
                                        break;
                                    default:
                                        // Genuinely unknown type — keep raw bytes for pass-through
                                        record.RawData = fullObj;
                                        break;
                                }
                            }
                            fullFidelity.AllObjectsInOrder.Add(record);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex) 
            { 
                WemAdditionLogger.LogException("ParseEventBnkFile_FullFidelity", ex);
                return false; 
            }
        }

        private static uint SkipToSection(BinaryReader reader, string sectionName, bool returnLength)
        {
            try {
                while (reader.BaseStream.Position < reader.BaseStream.Length) {
                    var name = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    uint length = reader.ReadUInt32();
                    if (name == sectionName) return length;
                    reader.BaseStream.Seek(length, SeekOrigin.Current);
                }
            } catch { }
            return 0;
        }
    }
}
