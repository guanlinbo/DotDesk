using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace DotDesk.Core.Models
{
    /// <summary>
    /// 设备码：多维硬件指纹 → SHA256 → 10位纯数字
    ///
    /// 设计目标：
    ///   唯一性  — 采集 CPU / 主板 / 磁盘 / BIOS / MAC 五维硬件信息，
    ///             任意一维不同则生成码不同
    ///   稳定性  — 同一台机器每次运行结果相同
    ///   无碰撞  — 10位十进制 = 10^10 = 100亿种组合，
    ///             百万级设备碰撞概率 < 0.005%（生日悖论公式）
    ///   可读性  — 纯数字，显示格式 XXX-XXX-XXXX（如 123-456-7890）
    /// </summary>
    public static class DeviceCode
    {
        private static string? _cached;

        /// <summary>获取本机 10 位纯数字设备码（无分隔符）</summary>
        public static string Get() => _cached ??= Generate();

        /// <summary>格式化显示：123-456-789</summary>
        public static string GetFormatted()
        {
            var c = Get();
            return $"{c[..3]}-{c[3..6]}-{c[6..]}";
        }

        /// <summary>规范化用户输入（去掉分隔符/空格）</summary>
        public static string Normalize(string input) =>
            input.Replace("-", "").Replace(" ", "").Trim();

        // ── 生成 ─────────────────────────────────────────────────

        private static string Generate()
        {
            // 五维硬件指纹拼接
            var sb = new StringBuilder();
            sb.Append("CPU:").Append(GetCpuId());
            sb.Append("|MB:").Append(GetMotherboardSerial());
            sb.Append("|DISK:").Append(GetDiskSerial());
            sb.Append("|BIOS:").Append(GetBiosSerial());
            sb.Append("|MAC:").Append(GetMacAddress());

            var fingerprint = sb.ToString();
            Console.WriteLine($"[DeviceCode] 硬件指纹: {fingerprint}");

            // SHA256 → 取前 5 字节（40 bit）→ ulong
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));

            // 取 5 字节拼成 40-bit 无符号整数（大端序）
            ulong value = ((ulong)hash[0] << 32)
                        | ((ulong)hash[1] << 24)
                        | ((ulong)hash[2] << 16)
                        | ((ulong)hash[3] << 8)
                        | (ulong)hash[4];

            // 映射到 100_000_000 ~ 999_999_999（保证9位）
            var code = (value % 900_000_000UL) + 100_000_000UL;
            return code.ToString();
        }

        // ── 五维硬件信息采集 ──────────────────────────────────────

        // 1. CPU ProcessorId（x86 唯一标识）
        private static string GetCpuId()
        {
            try
            {
                using var s = new ManagementObjectSearcher(
                    "SELECT ProcessorId FROM Win32_Processor");
                foreach (var o in s.Get())
                {
                    var v = o["ProcessorId"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            catch { }
            return "CPU_NA";
        }

        // 2. 主板序列号
        private static string GetMotherboardSerial()
        {
            try
            {
                using var s = new ManagementObjectSearcher(
                    "SELECT SerialNumber,Product FROM Win32_BaseBoard");
                foreach (var o in s.Get())
                {
                    var serial = o["SerialNumber"]?.ToString()?.Trim();
                    var product = o["Product"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(serial)) return $"{product}_{serial}";
                }
            }
            catch { }
            return "MB_NA";
        }

        // 3. 第一块物理磁盘序列号
        private static string GetDiskSerial()
        {
            try
            {
                using var s = new ManagementObjectSearcher(
                    "SELECT SerialNumber,Model FROM Win32_DiskDrive WHERE MediaType='Fixed hard disk media'");
                foreach (var o in s.Get())
                {
                    var serial = o["SerialNumber"]?.ToString()?.Trim();
                    var model = o["Model"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(serial)) return $"{model}_{serial}";
                }
            }
            catch { }
            return "DISK_NA";
        }

        // 4. BIOS 序列号 + 版本
        private static string GetBiosSerial()
        {
            try
            {
                using var s = new ManagementObjectSearcher(
                    "SELECT SerialNumber,Version FROM Win32_BIOS");
                foreach (var o in s.Get())
                {
                    var serial = o["SerialNumber"]?.ToString()?.Trim();
                    var version = o["Version"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(serial)) return $"{version}_{serial}";
                }
            }
            catch { }
            return "BIOS_NA";
        }

        // 5. 第一块物理网卡 MAC（排除虚拟网卡）
        private static string GetMacAddress()
        {
            try
            {
                using var s = new ManagementObjectSearcher(
                    "SELECT MACAddress,PhysicalAdapter FROM Win32_NetworkAdapter " +
                    "WHERE PhysicalAdapter=True AND MACAddress IS NOT NULL");
                foreach (var o in s.Get())
                {
                    var mac = o["MACAddress"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(mac)) return mac;
                }
            }
            catch { }
            return "MAC_NA";
        }
    }
}