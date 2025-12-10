using System;
using System.IO;

namespace LoL_Announcer_Editor
{
    public static class BnkLogger
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bnk_logs.txt");
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
                header += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [START] BNK Operation: {operation}\n";
                header += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [START] ========================================\n";
                File.AppendAllText(LogPath, header);
            }
        }

        public static void LogOperationEnd(string operation, bool success)
        {
            lock (LogLock)
            {
                string status = success ? "SUCCESS" : "FAILED";
                string footer = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [END] Operation {operation}: {status}\n";
                footer += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [END] ========================================\n";
                File.AppendAllText(LogPath, footer);
            }
        }

        public static void LogDebug(string message)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [DEBUG] {message}\n");
            }
        }
    }
} 