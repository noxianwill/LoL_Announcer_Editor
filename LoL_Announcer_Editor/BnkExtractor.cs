using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace LoL_Announcer_Editor
{
    public static class BnkExtractor
    {

        public static bool ParseEventBnkFile_FullFidelity(string path, BnkFullFidelity fullFidelity, BNKSections sections)
        {
            if (!File.Exists(path)) { Logger.LogBnkError(path); return false; }
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    bool hircFound = false;

                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        var sectionNameBytes = reader.ReadBytes(4);
                        if (sectionNameBytes.Length < 4) break;
                        string sectionName = Encoding.ASCII.GetString(sectionNameBytes);
                        uint sectionLen = reader.ReadUInt32();

                        if (sectionName == "BKHD")
                        {
                            fullFidelity.BkhdData = reader.ReadBytes((int)sectionLen);
                            using (var ms = new MemoryStream(fullFidelity.BkhdData))
                            using (var bkhdReader = new BinaryReader(ms))
                            {
                                fullFidelity.BnkVersion = bkhdReader.ReadUInt32();
                                sections.BnkVersion = fullFidelity.BnkVersion;
                            }
                        }
                        else if (sectionName == "HIRC")
                        {
                            hircFound = true;
                            byte[] hircData = reader.ReadBytes((int)sectionLen);
                            using (var ms = new MemoryStream(hircData))
                            using (var hircReader = new BinaryReader(ms))
                            {
                                uint numObjects = hircReader.ReadUInt32();
                                for (int i = 0; i < numObjects; i++)
                                {
                                    byte type = hircReader.ReadByte();
                                    uint len = hircReader.ReadUInt32();
                                    byte[] objPayload = hircReader.ReadBytes((int)len);
                                    byte[] fullObj = new byte[len + 5];
                                    fullObj[0] = type;
                                    Array.Copy(BitConverter.GetBytes(len), 0, fullObj, 1, 4);
                                    Array.Copy(objPayload, 0, fullObj, 5, len);

                                    var record = new BnkObjectRecord { Type = type, Length = len, RawData = fullObj };
                                    using (var objMs = new MemoryStream(objPayload))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        switch (type)
                                        {
                                            case 1:
                                                var setts = new List<Settings>();
                                                BnkReaders.ReadSettingsObject(objReader, setts, null);
                                                record.ParsedObject = setts.FirstOrDefault();
                                                record.ObjectId = setts.FirstOrDefault()?.SelfId;
                                                sections.Settings.AddRange(setts);
                                                break;
                                            case 2:
                                                var sounds = new List<Sound>();
                                                BnkReaders.ReadSoundObject(objReader, sounds, fullFidelity.BnkVersion, null);
                                                record.ParsedObject = sounds.FirstOrDefault();
                                                record.ObjectId = sounds.FirstOrDefault()?.SelfId;
                                                sections.Sounds.AddRange(sounds);
                                                break;
                                            case 3:
                                                var actions = new List<EventAction>();
                                                BnkReaders.ReadEventActionObject(objReader, actions, null);
                                                record.ParsedObject = actions.FirstOrDefault();
                                                record.ObjectId = actions.FirstOrDefault()?.SelfId;
                                                sections.EventActions.AddRange(actions);
                                                break;
                                            case 4:
                                                var events = new List<Event>();
                                                BnkReaders.ReadEventObject(objReader, events, fullFidelity.BnkVersion, null);
                                                record.ParsedObject = events.FirstOrDefault();
                                                record.ObjectId = events.FirstOrDefault()?.SelfId;
                                                sections.Events.AddRange(events);
                                                break;
                                            case 5:
                                                var containers = new List<RandomContainer>();
                                                BnkReaders.ReadRandomContainerObject(objReader, containers, fullFidelity.BnkVersion, null);
                                                record.ParsedObject = containers.FirstOrDefault();
                                                record.ObjectId = containers.FirstOrDefault()?.SelfId;
                                                sections.RandomContainers.AddRange(containers);
                                                break;
                                            case 6:
                                                var swContainers = new List<SwitchContainer>();
                                                BnkReaders.ReadSwitchContainerObject(objReader, swContainers, fullFidelity.BnkVersion, null);
                                                record.ParsedObject = swContainers.FirstOrDefault();
                                                record.ObjectId = swContainers.FirstOrDefault()?.SelfId;
                                                sections.SwitchContainers.AddRange(swContainers);
                                                break;
                                            case 7:
                                                var mixers = new List<ActorMixer>();
                                                BnkReaders.ReadActorMixerObject(objReader, mixers, fullFidelity.BnkVersion, null);
                                                record.ParsedObject = mixers.FirstOrDefault();
                                                record.ObjectId = mixers.FirstOrDefault()?.SelfId;
                                                sections.ActorMixers.AddRange(mixers);
                                                break;
                                            case 8:
                                                var audioBuses = new List<AudioBus>();
                                                BnkReaders.ReadAudioBusObject(objReader, audioBuses, null);
                                                record.ParsedObject = audioBuses.FirstOrDefault();
                                                record.ObjectId = audioBuses.FirstOrDefault()?.SelfId;
                                                sections.AudioBuses.AddRange(audioBuses);
                                                break;
                                            case 9:
                                                var blendContainers = new List<BlendContainer>();
                                                BnkReaders.ReadBlendContainerObject(objReader, blendContainers, fullFidelity.BnkVersion, null);
                                                record.ParsedObject = blendContainers.FirstOrDefault();
                                                record.ObjectId = blendContainers.FirstOrDefault()?.SelfId;
                                                sections.BlendContainers.AddRange(blendContainers);
                                                break;
                                            case 10:
                                                var musicSegs = new List<MusicSegment>();
                                                BnkReaders.ReadMusicContainerObject(objReader, musicSegs, fullFidelity.BnkVersion, null);
                                                record.ParsedObject = musicSegs.FirstOrDefault();
                                                record.ObjectId = musicSegs.FirstOrDefault()?.SelfId;
                                                sections.MusicSegments.AddRange(musicSegs);
                                                break;
                                            case 11:
                                                var musicTracks = new List<MusicTrack>();
                                                BnkReaders.ReadMusicTrackObject(objReader, musicTracks, fullFidelity.BnkVersion, null);
                                                record.ParsedObject = musicTracks.FirstOrDefault();
                                                record.ObjectId = musicTracks.FirstOrDefault()?.SelfId;
                                                sections.MusicTracks.AddRange(musicTracks);
                                                break;
                                            case 12:
                                                var musicSwitches = new List<MusicSwitch>();
                                                BnkReaders.ReadMusicSwitchObject(objReader, musicSwitches, fullFidelity.BnkVersion, null);
                                                record.ParsedObject = musicSwitches.FirstOrDefault();
                                                record.ObjectId = musicSwitches.FirstOrDefault()?.SelfId;
                                                sections.MusicSwitches.AddRange(musicSwitches);
                                                break;
                                            case 13:
                                                var musicPlaylists = new List<MusicPlaylist>();
                                                BnkReaders.ReadMusicContainerObject(objReader, musicPlaylists, fullFidelity.BnkVersion, null);
                                                record.ParsedObject = musicPlaylists.FirstOrDefault();
                                                record.ObjectId = musicPlaylists.FirstOrDefault()?.SelfId;
                                                sections.MusicPlaylists.AddRange(musicPlaylists);
                                                break;
                                            case 14:
                                                var attenuations = new List<Attenuation>();
                                                BnkReaders.ReadAttenuationObject(objReader, attenuations, null);
                                                record.ParsedObject = attenuations.FirstOrDefault();
                                                record.ObjectId = attenuations.FirstOrDefault()?.SelfId;
                                                sections.Attenuations.AddRange(attenuations);
                                                break;
                                            case 15:
                                                var dialogueEvents = new List<DialogueEvent>();
                                                BnkReaders.ReadDialogueEventObject(objReader, dialogueEvents, null);
                                                record.ParsedObject = dialogueEvents.FirstOrDefault();
                                                record.ObjectId = dialogueEvents.FirstOrDefault()?.SelfId;
                                                sections.DialogueEvents.AddRange(dialogueEvents);
                                                break;
                                            case 16:
                                                var motionBuses = new List<MotionBus>();
                                                BnkReaders.ReadMotionBusObject(objReader, motionBuses, null);
                                                record.ParsedObject = motionBuses.FirstOrDefault();
                                                record.ObjectId = motionBuses.FirstOrDefault()?.SelfId;
                                                sections.MotionBuses.AddRange(motionBuses);
                                                break;
                                            case 17:
                                                var motionFxs = new List<MotionFx>();
                                                BnkReaders.ReadMotionFxObject(objReader, motionFxs, null);
                                                record.ParsedObject = motionFxs.FirstOrDefault();
                                                record.ObjectId = motionFxs.FirstOrDefault()?.SelfId;
                                                sections.MotionFxs.AddRange(motionFxs);
                                                break;
                                            case 18:
                                                var effects = new List<Effect>();
                                                BnkReaders.ReadEffectObject(objReader, effects, null);
                                                record.ParsedObject = effects.FirstOrDefault();
                                                record.ObjectId = effects.FirstOrDefault()?.SelfId;
                                                sections.Effects.AddRange(effects);
                                                break;
                                            case 19:
                                                var auxBuses = new List<AuxiliaryBus>();
                                                BnkReaders.ReadAuxiliaryBusObject(objReader, auxBuses, null);
                                                record.ParsedObject = auxBuses.FirstOrDefault();
                                                record.ObjectId = auxBuses.FirstOrDefault()?.SelfId;
                                                sections.AuxiliaryBuses.AddRange(auxBuses);
                                                break;
                                            default:
                                                record.RawData = fullObj;
                                                break;
                                        }
                                    }
                                    fullFidelity.AllObjectsInOrder.Add(record);
                                }
                            }
                        }
                        else
                        {
                            var sectionData = reader.ReadBytes((int)sectionLen);
                            var section = new BnkSection { Name = sectionName, Data = sectionData };
                            if (hircFound) fullFidelity.PostHircSections.Add(section);
                            else fullFidelity.PreHircSections.Add(section);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex) 
            { 
                Logger.LogException("ParseEventBnkFile_FullFidelity", ex);
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
