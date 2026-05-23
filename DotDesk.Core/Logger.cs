using System;
using System.IO;

namespace DotDesk.Core
{
    /// <summary>
    /// 全局日志
    /// 文件位置：桌面/DotDesk_logs/dotdesk_yyyyMMdd.log
    /// </summary>
    public static class AppLogger
    {
        private static readonly string _logDir;
        private static readonly object _lock = new();

        static AppLogger()
        {
            // 写到桌面，方便查找
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "DotDesk_logs");

            Directory.CreateDirectory(_logDir);
            Console.WriteLine($"[Logger] 日志目录: {_logDir}");
        }

        private static string LogFilePath =>
            Path.Combine(_logDir, $"dotdesk_{DateTime.Now:yyyyMMdd}.log");

        /// <summary>写日志</summary>
        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Console.WriteLine(line);
            lock (_lock)
            {
                try { File.AppendAllText(LogFilePath, line + Environment.NewLine); }
                catch { }
            }
        }

        /// <summary>带分类写日志</summary>
        public static void Log(string category, string message) =>
            Log($"[{category}] {message}");

        /// <summary>获取今天的日志文件路径</summary>
        public static string GetLogFilePath() => LogFilePath;

        /// <summary>打开日志目录（资源管理器）</summary>
        public static void OpenLogDir()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", _logDir); }
            catch { }
        }
    }
}