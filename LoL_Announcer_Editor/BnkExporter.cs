using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoL_Announcer_Editor
{
    public static class BnkExporter
    {
        public static void ExportToJson(BNKSections sections, string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            
            string json = JsonSerializer.Serialize(sections, options);
            File.WriteAllText(filePath, json);
        }
    }
}
