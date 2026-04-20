using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace LoL_Announcer_Editor
{
    /// <summary>
    /// Loads original Sound metadata from a reference JSON file so that newly
    /// injected sounds can inherit correct plugin/stream/node settings.
    /// Adapted to use the flat Sound structure (SoundStructure wrapper removed).
    /// </summary>
    public class ReferenceSoundMapper
    {
        private static Dictionary<uint, uint> _originalSourceIds;
        private static Dictionary<uint, Sound> _originalSounds;
        private static bool _isInitialized = false;
        private static readonly string _referenceJsonPath = Path.Combine("Extra References", "original_announcer_global_female1_vo_events_parsed.json");

        public static void Initialize()
        {
            if (_isInitialized) return;
            _originalSourceIds = new Dictionary<uint, uint>();
            _originalSounds    = new Dictionary<uint, Sound>();
            try   { LoadReferenceData();       _isInitialized = true; }
            catch { InitializeDefaultMappings(); _isInitialized = true; }
        }

        private static void LoadReferenceData()
        {
            if (!File.Exists(_referenceJsonPath)) throw new FileNotFoundException();
            string jsonContent = File.ReadAllText(_referenceJsonPath);
            using (JsonDocument document = JsonDocument.Parse(jsonContent))
            {
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("id", out JsonElement idEl) && idEl.GetString() == "HIRC")
                    {
                        if (element.TryGetProperty("data", out JsonElement dataEl) &&
                            dataEl.TryGetProperty("objects", out JsonElement objectsEl))
                        {
                            foreach (JsonElement objEl in objectsEl.EnumerateArray())
                            {
                                if (objEl.TryGetProperty("type", out JsonElement typeEl) && typeEl.GetString() == "Sound")
                                    ProcessSoundObject(objEl);
                            }
                        }
                        break;
                    }
                }
            }
        }

        private static void ProcessSoundObject(JsonElement soundElement)
        {
            try
            {
                if (!soundElement.TryGetProperty("id", out JsonElement idEl)) return;
                uint soundId = idEl.GetUInt32();
                if (!soundElement.TryGetProperty("data", out JsonElement dataEl)) return;

                var s = new Sound();
                s.SelfId = soundId;
                if (dataEl.TryGetProperty("sourceId",  out var v)) s.SourceId  = v.GetUInt32();
                if (dataEl.TryGetProperty("pluginId",  out var p)) s.PluginId  = p.GetUInt32();
                if (dataEl.TryGetProperty("streamType",out var st)) s.StreamType = (byte)st.GetUInt32();
                if (dataEl.TryGetProperty("sourceBits",out var sb)) s.SourceBits = (byte)sb.GetUInt32();
                if (dataEl.TryGetProperty("inMemoryMediaSize", out var ms)) s.InMemoryMediaSize = ms.GetUInt32();

                _originalSourceIds[soundId] = s.SourceId;
                _originalSounds[soundId]    = s;
            }
            catch { }
        }

        private static void InitializeDefaultMappings()
        {
            _originalSourceIds = new Dictionary<uint, uint> { { 827862483, 631040 } };
            _originalSounds    = new Dictionary<uint, Sound>
            {
                { 827862483, new Sound { SelfId = 827862483, PluginId = 16777416, StreamType = 66, SourceBits = 1 } }
            };
        }

        public static uint GetOriginalSourceId(uint soundId)
        {
            Initialize();
            return _originalSourceIds.TryGetValue(soundId, out uint id) ? id : 0;
        }

        public static Sound GetOriginalSound(uint soundId)
        {
            Initialize();
            return _originalSounds.TryGetValue(soundId, out var s) ? s : null;
        }

        public static bool HasMapping(uint soundId) { Initialize(); return _originalSourceIds.ContainsKey(soundId); }
        public static int  GetMappingCount()        { Initialize(); return _originalSourceIds.Count; }
    }
}
