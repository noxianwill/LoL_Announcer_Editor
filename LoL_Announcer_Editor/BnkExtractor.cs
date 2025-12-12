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
            BnkLogger.LogOperationStart("Parse BNK File");
            BnkLogger.LogInfo($"ParseEventBnkFile: Starting to parse BNK file: {path}");
            
            if (!File.Exists(path))
            {
                BnkLogger.LogError($"ParseEventBnkFile: File not found: {path}");
                BnkLogger.LogOperationEnd("Parse BNK File", false);
                Defs.EPrintf($"Error: Failed to open \"{path}\".\n");
                return false;
            }
            
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                var magic = reader.ReadBytes(4);
                if (Encoding.ASCII.GetString(magic) != "BKHD")
                {
                    BnkLogger.LogError("ParseEventBnkFile: Not a valid BNK file (missing BKHD magic)");
                    BnkLogger.LogOperationEnd("Parse BNK File", false);
                    Defs.EPrintf("Error: Not a bnk file!\n");
                    return false;
                }
                
                BnkLogger.LogInfo("ParseEventBnkFile: Valid BKHD magic found");
                reader.BaseStream.Seek(4, SeekOrigin.Current);
                uint bnkVersion = reader.ReadUInt32();
                BnkLogger.LogInfo($"ParseEventBnkFile: BNK version: {bnkVersion:X}");

                uint sectionLength = SkipToSection(reader, "HIRC", true);
                if (sectionLength == 0)
                {
                    BnkLogger.LogError($"ParseEventBnkFile: Failed to find HIRC section in file: {path}");
                    BnkLogger.LogOperationEnd("Parse BNK File", false);
                    Defs.EPrintf($"Error: Failed to skip to section \"HIRC\" in file \"{path}\".\nMake sure to provide the correct file.\n");
                    return false;
                }
                
                BnkLogger.LogInfo($"ParseEventBnkFile: Found HIRC section, length: {sectionLength} bytes");
                long initialPosition = reader.BaseStream.Position;
                uint numOfObjects = reader.ReadUInt32();
                BnkLogger.LogInfo($"ParseEventBnkFile: HIRC section contains {numOfObjects} objects");
                
                uint objectsRead = 0;
                uint soundsRead = 0, eventActionsRead = 0, eventsRead = 0, randomContainersRead = 0;
                uint switchContainersRead = 0, actorMixersRead = 0, settingsRead = 0, audioBusesRead = 0, blendContainersRead = 0;
                uint attenuationsRead = 0, dialogueEventsRead = 0, motionBusesRead = 0, motionFxsRead = 0, effectsRead = 0, auxiliaryBusesRead = 0;
                uint musicSegmentsRead = 0, musicTracksRead = 0, musicSwitchesRead = 0, musicPlaylistsRead = 0, unknownTypesRead = 0;
                
                while (reader.BaseStream.Position < initialPosition + sectionLength)
                {
                    long objectStart = reader.BaseStream.Position;
                    byte type = reader.ReadByte();
                    uint objectLength = reader.ReadUInt32();
                    byte[] objectBytes = reader.ReadBytes((int)objectLength);
                    // Compose the full object bytes including type and length
                    byte[] fullObjectBytes = new byte[objectLength + 5];
                    fullObjectBytes[0] = type;
                    Array.Copy(BitConverter.GetBytes(objectLength), 0, fullObjectBytes, 1, 4);
                    Array.Copy(objectBytes, 0, fullObjectBytes, 5, objectLength);
                    using (var ms = new MemoryStream(objectBytes))
                    using (var objReader = new BinaryReader(ms))
                    {
                        switch (type)
                        {
                            case 1:
                                BnkObjectReaders.ReadSettingsObject(objReader, sections.Settings, fullObjectBytes);
                                settingsRead++;
                                break;
                            case 2:
                                BnkObjectReaders.ReadSoundObject(objReader, sections.Sounds, bnkVersion, fullObjectBytes);
                                soundsRead++;
                                break;
                            case 3:
                                BnkObjectReaders.ReadEventActionObject(objReader, sections.EventActions, fullObjectBytes);
                                eventActionsRead++;
                                break;
                            case 4:
                                BnkObjectReaders.ReadEventObject(objReader, sections.Events, bnkVersion, fullObjectBytes);
                                eventsRead++;
                                break;
                            case 5:
                                BnkObjectReaders.ReadRandomContainerObject(objReader, sections.RandomContainers, bnkVersion, fullObjectBytes);
                                randomContainersRead++;
                                break;
                            case 6:
                                BnkObjectReaders.ReadSwitchContainerObject(objReader, sections.SwitchContainers, bnkVersion, fullObjectBytes);
                                switchContainersRead++;
                                break;
                            case 7:
                                BnkObjectReaders.ReadActorMixerObject(objReader, sections.ActorMixers, bnkVersion, fullObjectBytes);
                                actorMixersRead++;
                                break;
                            case 8:
                                BnkObjectReaders.ReadAudioBusObject(objReader, sections.AudioBuses, fullObjectBytes);
                                audioBusesRead++;
                                break;
                            case 9:
                                BnkObjectReaders.ReadBlendContainerObject(objReader, sections.BlendContainers, fullObjectBytes);
                                blendContainersRead++;
                                break;
                            case 14:
                                BnkObjectReaders.ReadAttenuationObject(objReader, sections.Attenuations, fullObjectBytes);
                                attenuationsRead++;
                                break;
                            case 15:
                                BnkObjectReaders.ReadDialogueEventObject(objReader, sections.DialogueEvents, fullObjectBytes);
                                dialogueEventsRead++;
                                break;
                            case 16:
                                BnkObjectReaders.ReadMotionBusObject(objReader, sections.MotionBuses, fullObjectBytes);
                                motionBusesRead++;
                                break;
                            case 17:
                                BnkObjectReaders.ReadMotionFxObject(objReader, sections.MotionFxs, fullObjectBytes);
                                motionFxsRead++;
                                break;
                            case 18:
                                BnkObjectReaders.ReadEffectObject(objReader, sections.Effects, fullObjectBytes);
                                effectsRead++;
                                break;
                            case 19:
                                BnkObjectReaders.ReadAuxiliaryBusObject(objReader, sections.AuxiliaryBuses, fullObjectBytes);
                                auxiliaryBusesRead++;
                                break;
                            case 10:
                                BnkObjectReaders.ReadMusicContainerObject(objReader, sections.MusicSegments, bnkVersion, fullObjectBytes);
                                musicSegmentsRead++;
                                break;
                            case 11:
                                BnkObjectReaders.ReadMusicTrackObject(objReader, sections.MusicTracks, bnkVersion, fullObjectBytes);
                                musicTracksRead++;
                                break;
                            case 12:
                                BnkObjectReaders.ReadMusicSwitchObject(objReader, sections.MusicSwitches, bnkVersion, fullObjectBytes);
                                musicSwitchesRead++;
                                break;
                            case 13:
                                BnkObjectReaders.ReadMusicContainerObject(objReader, sections.MusicPlaylists, bnkVersion, fullObjectBytes);
                                musicPlaylistsRead++;
                                break;
                            default:
                                unknownTypesRead++;
                                break;
                        }
                    }
                    reader.BaseStream.Position = objectStart + 5 + objectLength;
                    objectsRead++;
                }
                
                BnkLogger.LogInfo($"ParseEventBnkFile: Object reading summary:");
                BnkLogger.LogInfo($"  - Total objects read: {objectsRead}/{numOfObjects}");
                BnkLogger.LogInfo($"  - Sounds: {soundsRead}");
                BnkLogger.LogInfo($"  - EventActions: {eventActionsRead}");
                BnkLogger.LogInfo($"  - Events: {eventsRead}");
                BnkLogger.LogInfo($"  - RandomContainers: {randomContainersRead}");
                BnkLogger.LogInfo($"  - SwitchContainers: {switchContainersRead}");
                BnkLogger.LogInfo($"  - ActorMixers: {actorMixersRead}");
                BnkLogger.LogInfo($"  - Settings: {settingsRead}");
                BnkLogger.LogInfo($"  - AudioBuses: {audioBusesRead}");
                BnkLogger.LogInfo($"  - BlendContainers: {blendContainersRead}");
                BnkLogger.LogInfo($"  - Attenuations: {attenuationsRead}");
                BnkLogger.LogInfo($"  - DialogueEvents: {dialogueEventsRead}");
                BnkLogger.LogInfo($"  - MotionBuses: {motionBusesRead}");
                BnkLogger.LogInfo($"  - MotionFxs: {motionFxsRead}");
                BnkLogger.LogInfo($"  - Effects: {effectsRead}");
                BnkLogger.LogInfo($"  - AuxiliaryBuses: {auxiliaryBusesRead}");
                BnkLogger.LogInfo($"  - MusicSegments: {musicSegmentsRead}");
                BnkLogger.LogInfo($"  - MusicTracks: {musicTracksRead}");
                BnkLogger.LogInfo($"  - MusicSwitches: {musicSwitchesRead}");
                BnkLogger.LogInfo($"  - MusicPlaylists: {musicPlaylistsRead}");
                BnkLogger.LogInfo($"  - Unknown types: {unknownTypesRead}");
                
                if (objectsRead != numOfObjects)
                {
                    BnkLogger.LogWarning($"ParseEventBnkFile: Object count mismatch - read {objectsRead} but expected {numOfObjects}");
                    Defs.EPrintf($"Warning: objects read ({objectsRead}) != num of objects ({numOfObjects})\n");
                }
                
                BnkLogger.LogInfo($"ParseEventBnkFile: Successfully parsed BNK file: {path}");
                BnkLogger.LogOperationEnd("Parse BNK File", true);
            }
            return true;
        }

        public static bool ParseEventBnkFile_FullFidelity(string path, BnkFullFidelity fullFidelity, BNKSections sections)
        {
            BnkLogger.LogOperationStart("Parse BNK File (Full Fidelity)");
            BnkLogger.LogInfo($"ParseEventBnkFile_FullFidelity: Starting to parse BNK file: {path}");
            if (!File.Exists(path))
            {
                BnkLogger.LogError($"ParseEventBnkFile_FullFidelity: File not found: {path}");
                BnkLogger.LogOperationEnd("Parse BNK File (Full Fidelity)", false);
                Defs.EPrintf($"Error: Failed to open \"{path}\".\n");
                return false;
            }
            
            uint bnkVersion = 0x86; // Default version
            
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                long fileLength = fs.Length;
                while (fs.Position < fileLength)
                {
                    long sectionStart = fs.Position;
                    string sectionId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    uint sectionLen = reader.ReadUInt32();
                    long sectionDataStart = fs.Position;
                    byte[] sectionData = reader.ReadBytes((int)sectionLen);
                    
                    // Read BNK version from BKHD section
                    if (sectionId == "BKHD")
                    {
                        using (var ms = new MemoryStream(sectionData))
                        using (var bkhdReader = new BinaryReader(ms))
                        {
                            bnkVersion = bkhdReader.ReadUInt32();
                            fullFidelity.BnkVersion = bnkVersion;
                            BnkLogger.LogInfo($"ParseEventBnkFile_FullFidelity: BNK version: {bnkVersion:X}");
                        }
                    }
                    
                    // If HIRC, parse objects inside (don't store the section itself)
                    if (sectionId == "HIRC")
                    {
                        BnkLogger.LogInfo($"ParseEventBnkFile_FullFidelity: Parsing HIRC section with {sectionLen} bytes");
                        using (var ms = new MemoryStream(sectionData))
                        using (var hircReader = new BinaryReader(ms))
                        {
                            uint count = hircReader.ReadUInt32();
                            BnkLogger.LogInfo($"ParseEventBnkFile_FullFidelity: HIRC contains {count} objects");
                            long initialPosition = ms.Position;
                            uint objectsRead = 0;
                            
                            while (ms.Position < initialPosition + sectionLen - 4) // -4 for the count we already read
                            {
                                long objStart = ms.Position;
                                byte type = hircReader.ReadByte();
                                uint len = hircReader.ReadUInt32();
                                byte[] objData = hircReader.ReadBytes((int)len);
                                
                                // Try to parse known types, else store as raw
                                object? parsed = null;
                                uint? objId = null;
                                if (type == 2) // Sound
                                {
                                    var sounds = new List<Sound>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadSoundObject(objReader, sounds, bnkVersion, objData);
                                    }
                                    if (sounds.Count > 0)
                                    {
                                        parsed = sounds[0];
                                        objId = sounds[0].SelfId;
                                    }
                                }
                                else if (type == 3) // EventAction
                                {
                                    var actions = new List<EventAction>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadEventActionObject(objReader, actions, objData);
                                    }
                                    if (actions.Count > 0)
                                    {
                                        parsed = actions[0];
                                        objId = actions[0].SelfId;
                                    }
                                }
                                else if (type == 4) // Event
                                {
                                    var events = new List<Event>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadEventObject(objReader, events, bnkVersion, objData);
                                    }
                                    if (events.Count > 0)
                                    {
                                        parsed = events[0];
                                        objId = events[0].SelfId;
                                    }
                                }
                                else if (type == 5) // SequenceContainer (often called RandomContainer)
                                {
                                    var randomContainers = new List<RandomContainer>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadRandomContainerObject(objReader, randomContainers, bnkVersion, objData);
                                    }
                                    if (randomContainers.Count > 0)
                                    {
                                        parsed = randomContainers[0];
                                        objId = randomContainers[0].SelfId;
                                    }
                                }
                                else if (type == 6) // SwitchContainer
                                {
                                    var switchContainers = new List<SwitchContainer>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadSwitchContainerObject(objReader, switchContainers, bnkVersion, objData);
                                    }
                                    if (switchContainers.Count > 0)
                                    {
                                        parsed = switchContainers[0];
                                        objId = switchContainers[0].SelfId;
                                    }
                                }
                                else if (type == 7) // ActorMixer
                                {
                                    var actorMixers = new List<ActorMixer>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadActorMixerObject(objReader, actorMixers, bnkVersion, objData);
                                    }
                                    if (actorMixers.Count > 0)
                                    {
                                        parsed = actorMixers[0];
                                        objId = actorMixers[0].SelfId;
                                    }
                                }
                                else if (type == 1) // Settings
                                {
                                    var settings = new List<Settings>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadSettingsObject(objReader, settings, objData);
                                    }
                                    if (settings.Count > 0)
                                    {
                                        parsed = settings[0];
                                        objId = settings[0].SelfId;
                                    }
                                }
                                else if (type == 8) // AudioBus
                                {
                                    var audioBuses = new List<AudioBus>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadAudioBusObject(objReader, audioBuses, objData);
                                    }
                                    if (audioBuses.Count > 0)
                                    {
                                        parsed = audioBuses[0];
                                        objId = audioBuses[0].SelfId;
                                    }
                                }
                                else if (type == 9) // BlendContainer
                                {
                                    var blends = new List<BlendContainer>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadBlendContainerObject(objReader, blends, objData);
                                    }
                                    if (blends.Count > 0)
                                    {
                                        parsed = blends[0];
                                        objId = blends[0].SelfId;
                                    }
                                }
                                else if (type == 14) // Attenuation
                                {
                                    var atts = new List<Attenuation>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadAttenuationObject(objReader, atts, objData);
                                    }
                                    if (atts.Count > 0)
                                    {
                                        parsed = atts[0];
                                        objId = atts[0].SelfId;
                                    }
                                }
                                else if (type == 15) // DialogueEvent
                                {
                                    var dels = new List<DialogueEvent>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadDialogueEventObject(objReader, dels, objData);
                                    }
                                    if (dels.Count > 0)
                                    {
                                        parsed = dels[0];
                                        objId = dels[0].SelfId;
                                    }
                                }
                                else if (type == 16) // MotionBus
                                {
                                    var mbs = new List<MotionBus>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadMotionBusObject(objReader, mbs, objData);
                                    }
                                    if (mbs.Count > 0)
                                    {
                                        parsed = mbs[0];
                                        objId = mbs[0].SelfId;
                                    }
                                }
                                else if (type == 17) // MotionFx
                                {
                                    var mfxs = new List<MotionFx>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadMotionFxObject(objReader, mfxs, objData);
                                    }
                                    if (mfxs.Count > 0)
                                    {
                                        parsed = mfxs[0];
                                        objId = mfxs[0].SelfId;
                                    }
                                }
                                else if (type == 18) // Effect
                                {
                                    var effs = new List<Effect>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadEffectObject(objReader, effs, objData);
                                    }
                                    if (effs.Count > 0)
                                    {
                                        parsed = effs[0];
                                        objId = effs[0].SelfId;
                                    }
                                }
                                else if (type == 19) // AuxiliaryBus
                                {
                                    var auxs = new List<AuxiliaryBus>();
                                    using (var objMs = new MemoryStream(objData))
                                    using (var objReader = new BinaryReader(objMs))
                                    {
                                        BnkObjectReaders.ReadAuxiliaryBusObject(objReader, auxs, objData);
                                    }
                                    if (auxs.Count > 0)
                                    {
                                        parsed = auxs[0];
                                        objId = auxs[0].SelfId;
                                    }
                                }
                                // ... handle other known types ...
                                fullFidelity.AllObjectsInOrder.Add(new BnkObjectRecord
                                {
                                    Type = type,
                                    Length = len,
                                    RawData = new byte[] { type }.Concat(BitConverter.GetBytes(len)).Concat(objData).ToArray(),
                                    ParsedObject = parsed,
                                    IsChanged = false,
                                    ObjectId = objId
                                });
                                
                                objectsRead++;
                                ms.Position = objStart + 5 + len; // Ensure we're at the right position
                            }
                            
                            if (objectsRead != count)
                            {
                                BnkLogger.LogWarning($"ParseEventBnkFile_FullFidelity: Object count mismatch - read {objectsRead} but expected {count}");
                            }
                        }
                    }
                    else
                    {
                        // Store non-HIRC sections as raw objects
                        fullFidelity.AllObjectsInOrder.Add(new BnkObjectRecord
                        {
                            Type = 0xFF, // Use 0xFF for section blocks
                            Length = sectionLen,
                            RawData = Encoding.ASCII.GetBytes(sectionId).Concat(BitConverter.GetBytes(sectionLen)).Concat(sectionData).ToArray(),
                            ParsedObject = null,
                            IsChanged = false,
                            ObjectId = null
                        });
                    }
                }
            }
            BnkLogger.LogInfo($"ParseEventBnkFile_FullFidelity: Successfully parsed {fullFidelity.AllObjectsInOrder.Count} objects");
            BnkLogger.LogOperationEnd("Parse BNK File (Full Fidelity)", true);
            return true;
        }

        // Helper to skip to a section by name (e.g., "HIRC")
        public static uint SkipToSection(BinaryReader reader, string name, bool fromBeginning)
        {
            if (fromBeginning)
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                long sectionStart = reader.BaseStream.Position;
                var sectionName = Encoding.ASCII.GetString(reader.ReadBytes(4));
                uint sectionLength = reader.ReadUInt32();
                if (sectionName == name)
                    return sectionLength;
                reader.BaseStream.Seek(sectionLength, SeekOrigin.Current);
            }
            return 0;
        }

        // Recursive function to add connected files (simplified for C#)
        public static void AddConnectedFiles(string str, uint id, uint parentId, List<StringHash> stringHashes, BNKSections sections)
        {
            // MusicSwitch
            var musicSwitch = sections.MusicSwitches.Find(ms => ms.SelfId == id);
            if (musicSwitch != null)
            {
                foreach (var child in musicSwitch.Children)
                    AddConnectedFiles(str, child, id, stringHashes, sections);
                return;
            }
            // MusicPlaylist
            var musicPlaylist = sections.MusicPlaylists.Find(mp => mp.SelfId == id);
            if (musicPlaylist != null)
            {
                foreach (var trackId in musicPlaylist.MusicTrackIds)
                    AddConnectedFiles(str, trackId, musicPlaylist.MusicTrackIds.Count > 1 ? id : parentId, stringHashes, sections);
                return;
            }
            // RandomContainer
            var randomContainer = sections.RandomContainers.Find(rc => rc.SelfId == id);
            if (randomContainer != null)
            {
                foreach (var soundId in randomContainer.SoundIds)
                    AddConnectedFiles(str, soundId, randomContainer.SoundIds.Count > 1 ? id : parentId, stringHashes, sections);
                return;
            }
            // SwitchContainer
            var switchContainer = sections.SwitchContainers.Find(sc => sc.SelfId == id);
            if (switchContainer != null)
            {
                foreach (var child in switchContainer.Children)
                    AddConnectedFiles(str, child, id, stringHashes, sections);
                return;
            }
            // MusicSegment
            var musicSegment = sections.MusicSegments.Find(ms => ms.SelfId == id);
            if (musicSegment != null)
            {
                foreach (var trackId in musicSegment.MusicTrackIds)
                {
                    var musicTrack = sections.MusicTracks.Find(mt => mt.SelfId == trackId);
                    if (musicTrack != null)
                    {
                        foreach (var fileId in musicTrack.FileIds)
                        {
                            stringHashes.Add(new StringHash
                            {
                                String = str,
                                Hash = fileId,
                                ContainerId = parentId,
                                MusicSegmentId = musicSegment.MusicTrackIds.Count > 1 ? musicSegment.SelfId : 0
                            });
                        }
                    }
                }
                return;
            }
            // Sounds
            var sound = sections.Sounds.Find(s => s.SelfId == id);
            if (sound != null)
            {
                stringHashes.Add(new StringHash
                {
                    String = str,
                    Hash = sound.FileId,
                    ContainerId = parentId,
                    SoundIndex = (uint)sections.Sounds.IndexOf(sound)
                });
                return;
            }
        }
    }
} 
