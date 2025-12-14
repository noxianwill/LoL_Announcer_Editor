using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace LoL_Announcer_Editor
{
    public class ReferenceSoundMapper
    {
        private static Dictionary<uint, uint> _originalStreamFileIDs;
        private static Dictionary<uint, SoundStructure> _originalSoundStructures;
        private static bool _isInitialized = false;
        private static readonly string _referenceJsonPath = Path.Combine("Extra References", "original_announcer_global_female1_vo_events_parsed.json");

        public static void Initialize()
        {
            if (_isInitialized)
            {
                return;
            }

            _originalStreamFileIDs = new Dictionary<uint, uint>();
            _originalSoundStructures = new Dictionary<uint, SoundStructure>();

            try
            {
                LoadReferenceData();
                _isInitialized = true;
            }
            catch
            {
                // Fall back to default values if reference file is not available
                InitializeDefaultMappings();
                _isInitialized = true;
            }
        }

        private static void LoadReferenceData()
        {
            if (!File.Exists(_referenceJsonPath))
            {
                throw new FileNotFoundException($"Reference JSON file not found: {_referenceJsonPath}");
            }

            string jsonContent = File.ReadAllText(_referenceJsonPath);
            using (JsonDocument document = JsonDocument.Parse(jsonContent))
            {
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("id", out JsonElement idElement) &&
                        idElement.GetString() == "HIRC")
                    {
                        if (element.TryGetProperty("data", out JsonElement dataElement) &&
                            dataElement.TryGetProperty("objects", out JsonElement objectsElement))
                        {
                            foreach (JsonElement objElement in objectsElement.EnumerateArray())
                            {
                                if (objElement.TryGetProperty("type", out JsonElement typeElement) &&
                                    typeElement.GetString() == "Sound")
                                {
                                    ProcessSoundObject(objElement);
                                }
                            }
                        }
                        break; // Found HIRC section, no need to continue
                    }
                }
            }
        }

        private static void ProcessSoundObject(JsonElement soundElement)
        {
            try
            {
                // Extract Sound ID
                if (!soundElement.TryGetProperty("id", out JsonElement idElement))
                {
                    return;
                }

                uint soundId = idElement.GetUInt32();

                // Extract data section
                if (!soundElement.TryGetProperty("data", out JsonElement dataElement))
                {
                    return;
                }

                // Extract streamFileID
                if (dataElement.TryGetProperty("streamFileID", out JsonElement streamFileIDElement))
                {
                    uint streamFileID = streamFileIDElement.GetUInt32();
                    _originalStreamFileIDs[soundId] = streamFileID;
                }

                // Extract soundStructure
                if (dataElement.TryGetProperty("soundStructure", out JsonElement soundStructureElement))
                {
                    var soundStructure = new SoundStructure
                    {
                        PluginID = GetUInt32Property(soundStructureElement, "pluginID"),
                        Type = (ushort)GetUInt32Property(soundStructureElement, "type"),
                        Company = (ushort)GetUInt32Property(soundStructureElement, "company"),
                        StreamType = (byte)GetUInt32Property(soundStructureElement, "streamType"),
                        SourceID = (int)GetUInt32Property(soundStructureElement, "sourceID"),
                        InMemoryMediaSize = (int)GetUInt32Property(soundStructureElement, "inMemoryMediaSize"),
                        SourceBits = (byte)GetUInt32Property(soundStructureElement, "sourceBits"),
                        IsLanguageSpecific = GetBoolProperty(soundStructureElement, "isLanguageSpecific"),
                        Prefetch = GetBoolProperty(soundStructureElement, "prefetch"),
                        NonCachable = GetBoolProperty(soundStructureElement, "nonCachable"),
                        HasSource = GetBoolProperty(soundStructureElement, "hasSource"),
                        BIsOverrideParentFX = (byte)GetUInt32Property(soundStructureElement, "bIsOverrideParentFX"),
                        NumFx = (byte)GetUInt32Property(soundStructureElement, "numFx"),
                        BIsOverrideParentMetadata = (byte)GetUInt32Property(soundStructureElement, "bIsOverrideParentMetadata"),
                        NumPluginMetadata = (byte)GetUInt32Property(soundStructureElement, "numPluginMetadata"),
                        BOverrideAttachmentParams = (byte)GetUInt32Property(soundStructureElement, "bOverrideAttachmentParams"),
                        OverrideBusID = GetUInt32Property(soundStructureElement, "overrideBusID"),
                        DirectParentID = GetUInt32Property(soundStructureElement, "directParentID")
                    };

                    _originalSoundStructures[soundId] = soundStructure;
                }
            }
            catch
            {
                // Failed to process Sound object, silently ignore
            }
        }

        private static uint GetUInt32Property(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement propElement))
            {
                return propElement.GetUInt32();
            }
            return 0;
        }

        private static bool GetBoolProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement propElement))
            {
                return propElement.GetBoolean();
            }
            return false;
        }

        private static void InitializeDefaultMappings()
        {
            // Fallback to the original hardcoded mappings if reference file is not available
            _originalStreamFileIDs = new Dictionary<uint, uint>
            {
                { 827862483, 631040 },
                { 607971742, 769565 },
                { 86994959, 769565 }
            };

            _originalSoundStructures = new Dictionary<uint, SoundStructure>
            {
                { 827862483, new SoundStructure
                    {
                        PluginID = 16777416,
                        Type = 7,
                        Company = 51200,
                        StreamType = 66,
                        SourceID = 0,
                        InMemoryMediaSize = 0,
                        SourceBits = 1,
                        IsLanguageSpecific = true,
                        Prefetch = false,
                        NonCachable = false,
                        HasSource = false,
                        BIsOverrideParentFX = 0,
                        NumFx = 0,
                        BIsOverrideParentMetadata = 0
                    }
                },
                { 607971742, new SoundStructure
                    {
                        PluginID = 16777437,
                        Type = 7,
                        Company = 46080,
                        StreamType = 66,
                        SourceID = 0,
                        InMemoryMediaSize = 16777216,
                        SourceBits = 1,
                        IsLanguageSpecific = true,
                        Prefetch = false,
                        NonCachable = false,
                        HasSource = false,
                        BIsOverrideParentFX = 0,
                        NumFx = 0,
                        BIsOverrideParentMetadata = 1
                    }
                },
                { 86994959, new SoundStructure
                    {
                        PluginID = 16777550,
                        Type = 7,
                        Company = 46080,
                        StreamType = 66,
                        SourceID = 0,
                        InMemoryMediaSize = 16777216,
                        SourceBits = 1,
                        IsLanguageSpecific = true,
                        Prefetch = false,
                        NonCachable = false,
                        HasSource = false,
                        BIsOverrideParentFX = 0,
                        NumFx = 0,
                        BIsOverrideParentMetadata = 1
                    }
                }
            };
        }

        public static uint GetOriginalStreamFileID(uint soundId)
        {
            Initialize();

            if (_originalStreamFileIDs.TryGetValue(soundId, out uint originalID))
            {
                return originalID;
            }

            return _originalStreamFileIDs.Values.FirstOrDefault(); // Return first available value as default
        }

        public static SoundStructure GetOriginalSoundStructure(uint soundId)
        {
            Initialize();

            if (_originalSoundStructures.TryGetValue(soundId, out SoundStructure originalStructure))
            {
                return originalStructure;
            }

            return _originalSoundStructures.Values.FirstOrDefault(); // Return first available structure as default
        }

        public static bool HasMapping(uint soundId)
        {
            Initialize();
            return _originalStreamFileIDs.ContainsKey(soundId) && _originalSoundStructures.ContainsKey(soundId);
        }

        public static int GetMappingCount()
        {
            Initialize();
            return _originalStreamFileIDs.Count;
        }
    }
}
