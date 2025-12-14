using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LoL_Announcer_Editor
{
    /// <summary>
    /// Targeted logger for diagnosing WEM addition issues.
    /// Logs only the specific operations related to adding WEMs and saving/loading files.
    /// </summary>
    public static class WemAdditionLogger
    {
        private static readonly object _lock = new object();
        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "wem_addition_debug.txt"
        );

        public static bool IsEnabled { get; set; } = true;

        public static void Clear()
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(LogPath, $"=== WEM Addition Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
            }
            catch { }
        }

        public static void Log(string message)
        {
            if (!IsEnabled) return;
            try
            {
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
                }
            }
            catch { }
        }

        public static void LogSection(string title)
        {
            Log($"\n{'='.ToString().PadRight(60, '=')}\n{title}\n{'='.ToString().PadRight(60, '=')}\n");
        }

        public static void LogSoundObject(Sound sound, string context)
        {
            if (!IsEnabled) return;
            var sb = new StringBuilder();
            sb.AppendLine($"  [{context}] Sound Object:");
            sb.AppendLine($"    SelfId: {sound.SelfId}");
            sb.AppendLine($"    FileId: {sound.FileId}");
            sb.AppendLine($"    StreamFileID: {sound.StreamFileID}");
            sb.AppendLine($"    IsStreamed: {sound.IsStreamed}");
            sb.AppendLine($"    RawData Length: {sound.RawData?.Length ?? -1}");
            if (sound.RawData != null && sound.RawData.Length >= 21)
            {
                sb.AppendLine($"    RawData[5-8] (SelfId): {BitConverter.ToUInt32(sound.RawData, 5)}");
                sb.AppendLine($"    RawData[14-17] (FileId): {BitConverter.ToUInt32(sound.RawData, 14)}");
                if (sound.RawData.Length > 20)
                    sb.AppendLine($"    RawData[17-20] (StreamFileID): {BitConverter.ToUInt32(sound.RawData, 17)}");
            }
            Log(sb.ToString());
        }

        public static void LogRandomContainer(RandomContainer rc, string context)
        {
            if (!IsEnabled) return;
            var sb = new StringBuilder();
            sb.AppendLine($"  [{context}] RandomContainer:");
            sb.AppendLine($"    SelfId: {rc.SelfId}");
            sb.AppendLine($"    SoundIdAmount: {rc.SoundIdAmount}");
            sb.AppendLine($"    SoundIds.Count: {rc.SoundIds.Count}");
            sb.AppendLine($"    Playlist.Count: {rc.Playlist.Count}");
            sb.AppendLine($"    RawData Length: {rc.RawData?.Length ?? -1}");
            sb.AppendLine($"    SoundIds: [{string.Join(", ", rc.SoundIds)}]");
            for (int i = 0; i < rc.Playlist.Count; i++)
            {
                sb.AppendLine($"    Playlist[{i}]: PlayID={rc.Playlist[i].PlayID}, Weight={rc.Playlist[i].Weight}");
            }
            Log(sb.ToString());
        }

        public static void LogEvent(Event evt, string context)
        {
            if (!IsEnabled) return;
            var sb = new StringBuilder();
            sb.AppendLine($"  [{context}] Event:");
            sb.AppendLine($"    SelfId: {evt.SelfId}");
            sb.AppendLine($"    EventAmount: {evt.EventAmount}");
            sb.AppendLine($"    EventIds: [{string.Join(", ", evt.EventIds)}]");
            Log(sb.ToString());
        }

        public static void LogEventAction(EventAction ea, string context)
        {
            if (!IsEnabled) return;
            var sb = new StringBuilder();
            sb.AppendLine($"  [{context}] EventAction:");
            sb.AppendLine($"    SelfId: {ea.SelfId}");
            sb.AppendLine($"    Scope: {ea.Scope}");
            sb.AppendLine($"    Type: {ea.Type}");
            sb.AppendLine($"    SoundObjectId: {ea.SoundObjectId}");
            Log(sb.ToString());
        }

        public static void LogBnkObjectRecord(BnkObjectRecord obj, string context)
        {
            if (!IsEnabled) return;
            var sb = new StringBuilder();
            sb.AppendLine($"  [{context}] BnkObjectRecord:");
            sb.AppendLine($"    Type: {obj.Type}");
            sb.AppendLine($"    Length: {obj.Length}");
            sb.AppendLine($"    ObjectId: {obj.ObjectId}");
            sb.AppendLine($"    IsChanged: {obj.IsChanged}");
            sb.AppendLine($"    RawData Length: {obj.RawData?.Length ?? -1}");
            sb.AppendLine($"    ParsedObject Type: {obj.ParsedObject?.GetType().Name ?? "null"}");
            Log(sb.ToString());
        }

        public static void LogWpkEntry(uint wemId, int slotIndex, int dataLength, string context)
        {
            if (!IsEnabled) return;
            Log($"  [{context}] WPK Entry: WemId={wemId}, SlotIndex={slotIndex}, DataLength={dataLength}");
        }
    }
}
