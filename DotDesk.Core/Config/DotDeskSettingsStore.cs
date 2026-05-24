using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DotDesk.Core.Config
{
    /// <summary>
    /// 本机轻量配置。用于保存固定访问密码和真实最近连接记录。
    /// </summary>
    public sealed class DotDeskSettings
    {
        public string? FixedPassword { get; set; }
        public List<RecentConnectionRecord> RecentConnections { get; set; } = new();
    }

    public sealed class RecentConnectionRecord
    {
        public string Code { get; set; } = "";
        public string DisplayCode { get; set; } = "";
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        public DateTime LastConnectedAt { get; set; }
    }

    public static class DotDeskSettingsStore
    {
        private static readonly object _lock = new();
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DotDesk",
            "settings.json");

        public static DotDeskSettings Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(SettingsPath)) return new DotDeskSettings();

                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<DotDeskSettings>(json) ?? new DotDeskSettings();
                }
                catch
                {
                    return new DotDeskSettings();
                }
            }
        }

        public static void Save(DotDeskSettings settings)
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, _jsonOptions));
            }
        }

        public static string? UpdateFixedPassword(string? password)
        {
            var normalized = NormalizePassword(password);
            var settings = Load();
            settings.FixedPassword = normalized;
            Save(settings);
            return normalized;
        }

        public static void AddRecentConnection(string code, string? name, string? address)
        {
            var normalized = NormalizeCode(code);
            if (normalized.Length != 9) return;

            var settings = Load();
            settings.RecentConnections.RemoveAll(x => NormalizeCode(x.Code) == normalized);
            settings.RecentConnections.Insert(0, new RecentConnectionRecord
            {
                Code = normalized,
                DisplayCode = FormatCode(normalized),
                Name = string.IsNullOrWhiteSpace(name) ? $"远程设备 {FormatCode(normalized)}" : name.Trim(),
                Address = string.IsNullOrWhiteSpace(address) ? FormatCode(normalized) : address.Trim(),
                LastConnectedAt = DateTime.Now
            });

            settings.RecentConnections = settings.RecentConnections
                .OrderByDescending(x => x.LastConnectedAt)
                .Take(10)
                .ToList();
            Save(settings);
        }

        public static string NormalizeCode(string? code) =>
            (code ?? "").Replace("-", "").Replace(" ", "").Trim();

        public static string FormatCode(string code)
        {
            var normalized = NormalizeCode(code);
            return normalized.Length == 9
                ? $"{normalized[..3]}-{normalized[3..6]}-{normalized[6..]}"
                : normalized;
        }

        public static string? NormalizePassword(string? password)
        {
            var clean = (password ?? "").Replace("-", "").Replace(" ", "").Trim().ToLowerInvariant();
            if (clean.Length != 6) return null;
            return clean.All(char.IsLetterOrDigit) ? clean : null;
        }
    }
}
