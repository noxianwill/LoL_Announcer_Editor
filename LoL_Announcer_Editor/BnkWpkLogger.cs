using System;
using System.IO;

namespace LoL_Announcer_Editor
{
    public static class BnkWpkLogger
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bnk_wpk_log.txt");
        private static readonly object LogLock = new object();

        public static void LogInfo(string message)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO] {message}\n");
            }
        }

        public static void LogError(string message)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ERROR] {message}\n");
            }
        }

        public static void LogWarning(string message)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [WARN] {message}\n");
            }
        }

        public static void LogOperationStart(string operation)
        {
            lock (LogLock)
            {
                string header = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [START] ========================================\n";
                header += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [START] BNK/WPK Operation: {operation}\n";
                header += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [START] ========================================\n";
                File.AppendAllText(LogPath, header);
            }
        }

        public static void LogOperationEnd(string operation, bool success = true)
        {
            lock (LogLock)
            {
                string status = success ? "SUCCESS" : "FAILED";
                string footer = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [END] Operation {operation}: {status}\n";
                footer += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [END] ========================================\n";
                File.AppendAllText(LogPath, footer);
            }
        }
    }

    public static class ScenarioLogger
    {
        private static readonly string ScenarioLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scenario_log.txt");
        private static readonly object ScenarioLock = new object();

        public static void StartScenario(string name)
        {
            lock (ScenarioLock)
            {
                var header = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [SCENARIO START] {name}\n" +
                             $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ========================================\n";
                File.AppendAllText(ScenarioLogPath, header);
            }
        }

        public static void EndScenario(bool success)
        {
            lock (ScenarioLock)
            {
                var status = success ? "SUCCESS" : "FAILED";
                var footer = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [SCENARIO END] {status}\n" +
                             $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ========================================\n";
                File.AppendAllText(ScenarioLogPath, footer);
            }
        }

        public static void LogSection(string name)
        {
            lock (ScenarioLock)
            {
                File.AppendAllText(ScenarioLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [SECTION] {name}\n");
            }
        }

        public static void LogInfo(string message)
        {
            lock (ScenarioLock)
            {
                File.AppendAllText(ScenarioLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO] {message}\n");
            }
        }

        public static void LogWarning(string message)
        {
            lock (ScenarioLock)
            {
                File.AppendAllText(ScenarioLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [WARN] {message}\n");
            }
        }

        public static void LogError(string message)
        {
            lock (ScenarioLock)
            {
                File.AppendAllText(ScenarioLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ERROR] {message}\n");
            }
        }

        public static void LogFileSize(string label, string path)
        {
            try
            {
                var info = new FileInfo(path);
                LogInfo($"{label} size: {info.Length} bytes");
            }
            catch (Exception ex)
            {
                LogWarning($"{label} size unavailable: {ex.Message}");
            }
        }
    }
}
