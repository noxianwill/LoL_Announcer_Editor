using System;
using System.IO;

namespace LoL_Announcer_Editor
{
    public static class BnkDebugLogger
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bnk_structure_debug.txt");
        private static readonly object LogLock = new object();

        public static void StartNewSession()
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"\n{'='} NEW DEBUG SESSION {'='}\n");
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SESSION START\n");
                File.AppendAllText(LogPath, $"{'='}\n\n");
            }
        }

        public static void Log(string message)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
        }

        public static void LogSection(string sectionName)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"\n{'-'} {sectionName} {'-'}\n");
            }
        }

        public static void LogEvent(string eventName, uint eventId, Event eventObj)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"\nEvent: {eventName}\n");
                File.AppendAllText(LogPath, $"  ID: {eventId}\n");
                File.AppendAllText(LogPath, $"  EventAmount: {eventObj.EventAmount}\n");
                File.AppendAllText(LogPath, $"  EventAction IDs: [{string.Join(", ", eventObj.EventIds)}]\n");
            }
        }

        public static void LogEventAction(EventAction ea)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"  EventAction ID: {ea.SelfId}\n");
                File.AppendAllText(LogPath, $"    Scope: {ea.Scope}\n");
                File.AppendAllText(LogPath, $"    Type: {ea.Type}\n");
                File.AppendAllText(LogPath, $"    SoundObjectId: {ea.SoundObjectId}\n");
            }
        }

        public static void LogRandomContainer(RandomContainer rc)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"    RandomContainer ID: {rc.SelfId}\n");
                File.AppendAllText(LogPath, $"      SoundIdAmount: {rc.SoundIdAmount}\n");
                File.AppendAllText(LogPath, $"      Sound IDs: [{string.Join(", ", rc.SoundIds)}]\n");
            }
        }

        public static void LogSound(Sound s)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"      Sound ID: {s.SelfId}\n");
                File.AppendAllText(LogPath, $"        FileId (WEM): {s.FileId}\n");
            }
        }

        public static void LogComparison(string label, object originalValue, object newValue)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"  {label}:\n");
                File.AppendAllText(LogPath, $"    ORIGINAL: {originalValue}\n");
                File.AppendAllText(LogPath, $"    MODIFIED: {newValue}\n");
                if (!originalValue.Equals(newValue))
                {
                    File.AppendAllText(LogPath, $"    >>> CHANGED <<<\n");
                }
            }
        }
    }
}
