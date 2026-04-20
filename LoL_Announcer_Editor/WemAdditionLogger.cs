using System;
using System.IO;
using System.Text;

namespace LoL_Announcer_Editor
{
    public static class WemAdditionLogger
    {
        private static string _logPath = "wem_additions.log";
        private static object _lock = new object();

        static WemAdditionLogger()
        {
            try
            {
                if (File.Exists(_logPath)) File.Delete(_logPath);
                Log("WEM Addition Logger Initialized");
                Log("--------------------------------");
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

        public static void StartNewSession()  => Log("\n--- NEW SESSION ---");
        public static void LogError(string m)   => Log($"ERROR: {m}");
        public static void LogSuccess(string m) => Log($"SUCCESS: {m}");
        public static void LogSection(string t) => Log($"\n=== {t} ===");

        public static void LogException(string context, Exception ex)
        {
            Log($"EXCEPTION in {context}: {ex.Message}");
            Log($"Stack Trace: {ex.StackTrace}");
            if (ex.InnerException != null) Log($"Inner Exception: {ex.InnerException.Message}");
        }

        public static void LogObject(string context, object obj)
        {
            if (obj is Sound sound) LogSound(context, sound);
            else if (obj is RandomContainer rc) LogRandomContainer(context, rc);
            else Log($"  [{context}] {obj.GetType().Name}: (no specific logger)");
        }

        private static void LogSound(string context, Sound sound)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  [{context}] Sound:");
            sb.AppendLine($"    SelfId:     {sound.SelfId}");
            sb.AppendLine($"    SourceId:   {sound.SourceId}");
            sb.AppendLine($"    StreamType: {sound.StreamType}");
            Log(sb.ToString());
        }

        private static void LogRandomContainer(string context, RandomContainer rc)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  [{context}] RandomContainer:");
            sb.AppendLine($"    SelfId:   {rc.SelfId}");
            sb.AppendLine($"    Sounds:   {rc.SoundIds.Count}");
            sb.AppendLine($"    Playlist: {rc.Playlist.Count}");
            Log(sb.ToString());
        }
    }
}
