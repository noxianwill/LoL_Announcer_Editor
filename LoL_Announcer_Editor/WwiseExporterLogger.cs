using System;
using System.IO;

namespace LoL_Announcer_Editor
{
    public static class WwiseExporterLogger
    {
        private static string _logPath = "wwise_export.log";
        private static object _lock = new object();

        static WwiseExporterLogger()
        {
            try
            {
                if (File.Exists(_logPath)) File.Delete(_logPath);
                Log("Wwise Project Exporter Logger Initialized");
                Log("------------------------------------------");
            }
            catch { }
        }

        public static void Log(string message)
        {
            try
            {
                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                lock (_lock) File.AppendAllText(_logPath, $"[{ts}] {message}\n");
            }
            catch { }
        }

        public static void LogSection(string t) => Log($"\n=== {t} ===");
        public static void LogError(string m) => Log($"ERROR: {m}");
    }
}
