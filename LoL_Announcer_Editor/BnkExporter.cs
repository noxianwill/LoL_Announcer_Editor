using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoL_Announcer_Editor
{
    public static class BnkExporter
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        public static void ExportToJson(BnkFullFidelity bnk, string filePath)
        {
            try
            {
                string json = JsonSerializer.Serialize(bnk, _options);
                File.WriteAllText(filePath, json);
                WwiseExporterLogger.Log($"Full BNK dump to JSON successful: {filePath}");
            }
            catch (Exception ex)
            {
                WwiseExporterLogger.LogError($"Failed to export BNK to JSON: {ex.Message}");
                throw;
            }
        }

        // Deprecated or kept for internal use if needed
        public static void ExportToSimplifiedJson(BNKSections sections, Dictionary<uint, string> eventNameLookup, string filePath)
        {
             // ... previous simplified logic if you still want it ...
        }
    }
}
