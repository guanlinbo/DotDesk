using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using DotDesk.Core.Logging;
using Sdcb.FFmpeg.Codecs;

namespace DotDesk.Client.Encoder
{
    public static class EncoderFactory
    {
        private sealed record GpuInfo(string Name, string Driver, string RamText, bool IsVirtual);

        private sealed record GpuProbeResult(
            bool IsReliable,
            bool HasNvidia,
            bool HasAmd,
            bool HasIntel,
            IReadOnlyList<GpuInfo> Gpus);

        public static IVideoEncoder Create(VideoEncoderOptions options, Action<string>? log = null)
        {
            void Log(string message)
            {
                AppLogger.Log("EncoderFactory", message);
                log?.Invoke(message);
            }

            Log($"开始选择视频编码器: codec={options.CodecFormat} mode={options.ConnectionMode} " +
                $"input={options.InputPixelFormat} {options.Width}x{options.Height} " +
                $"fps={options.TargetFps} bitrate={options.TargetBitrate / 1000}kbps gop={options.GopSize}");

            var gpuProbe = ProbeGpuInfo(Log);
            LogGpuProbeResult(gpuProbe, Log);
            LogFfmpegEncoderSupport(Log);

            if (TryCreateHardware("NVIDIA NVENC", HardwareVideoEncoderKind.Nvenc, options, gpuProbe, Log, out var encoder))
                return encoder;

            if (TryCreateHardware("AMD AMF", HardwareVideoEncoderKind.Amf, options, gpuProbe, Log, out encoder))
                return encoder;

            if (TryCreateHardware("Intel Quick Sync/QSV", HardwareVideoEncoderKind.Qsv, options, gpuProbe, Log, out encoder))
                return encoder;

            Log("硬件编码器不可用，fallback 到 x264 软件编码");
            var x264 = new X264VideoEncoder(options);
            Log($"最终选择编码器: {x264.Name}, hardware={x264.IsHardware}");
            return x264;
        }

        private static bool TryCreateHardware(
            string displayName,
            HardwareVideoEncoderKind kind,
            VideoEncoderOptions options,
            GpuProbeResult gpuProbe,
            Action<string> log,
            out IVideoEncoder encoder)
        {
            encoder = null!;
            log($"探测 {displayName}: 开始");

            if (!IsGpuVendorPresent(kind, gpuProbe, out var skipReason))
            {
                log($"探测 {displayName}: 跳过，{skipReason}");
                return false;
            }

            var codecName = GetCodecName(kind);
            if (Codec.FindEncoderByName(codecName) == null)
            {
                log($"探测 {displayName}: FFmpeg 不支持 {codecName}");
                return false;
            }

            try
            {
                encoder = kind switch
                {
                    HardwareVideoEncoderKind.Nvenc => new NvencVideoEncoder(options),
                    HardwareVideoEncoderKind.Amf => new AmfVideoEncoder(options),
                    HardwareVideoEncoderKind.Qsv => new QsvVideoEncoder(options),
                    _ => throw new NotSupportedException($"未知硬件编码器: {kind}")
                };

                log($"最终选择编码器: {encoder.Name}, hardware={encoder.IsHardware}");
                return true;
            }
            catch (Exception ex)
            {
                log($"探测 {displayName}: 初始化失败，{ex.GetType().Name}: {ex.Message}");
                SafeDispose(encoder);
                encoder = null!;
                return false;
            }
        }

        private static bool IsGpuVendorPresent(HardwareVideoEncoderKind kind, GpuProbeResult gpuProbe, out string reason)
        {
            reason = string.Empty;

            if (kind == HardwareVideoEncoderKind.Qsv && !IsQsvEnabled())
            {
                reason = "QSV 当前默认关闭，避免 Intel/驱动/FFmpeg 组合在 native 初始化阶段直接退出进程；需要测试时设置 DOTDESK_ENABLE_QSV=1";
                return false;
            }

            if (!gpuProbe.IsReliable)
            {
                reason = "GPU 厂商探测不可用，为避免 FFmpeg 硬件初始化导致进程崩溃，本次跳过硬件编码";
                return false;
            }

            var present = kind switch
            {
                HardwareVideoEncoderKind.Nvenc => gpuProbe.HasNvidia,
                HardwareVideoEncoderKind.Amf => gpuProbe.HasAmd,
                HardwareVideoEncoderKind.Qsv => gpuProbe.HasIntel,
                _ => false
            };

            if (present)
                return true;

            reason = kind switch
            {
                HardwareVideoEncoderKind.Nvenc => "未检测到 NVIDIA 物理显卡，不初始化 h264_nvenc",
                HardwareVideoEncoderKind.Amf => "未检测到 AMD/Radeon 物理显卡，不初始化 h264_amf",
                HardwareVideoEncoderKind.Qsv => "未检测到 Intel 核显/独显，不初始化 h264_qsv",
                _ => "未知硬件编码器"
            };

            return false;
        }

        private static string GetCodecName(HardwareVideoEncoderKind kind) => kind switch
        {
            HardwareVideoEncoderKind.Nvenc => "h264_nvenc",
            HardwareVideoEncoderKind.Amf => "h264_amf",
            HardwareVideoEncoderKind.Qsv => "h264_qsv",
            _ => string.Empty
        };

        private static bool IsQsvEnabled()
        {
            var value = Environment.GetEnvironmentVariable("DOTDESK_ENABLE_QSV");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static void LogFfmpegEncoderSupport(Action<string> log)
        {
            LogCodecSupport("h264_nvenc", log);
            LogCodecSupport("h264_amf", log);
            LogCodecSupport("h264_qsv", log);
        }

        private static void LogCodecSupport(string codecName, Action<string> log)
        {
            try
            {
                log($"FFmpeg 编码器 {codecName}: {(Codec.FindEncoderByName(codecName) != null ? "支持" : "不支持")}");
            }
            catch (Exception ex)
            {
                log($"FFmpeg 编码器 {codecName}: 探测失败，{ex.Message}");
            }
        }

        private static void LogGpuProbeResult(GpuProbeResult result, Action<string> log)
        {
            if (!result.IsReliable)
            {
                log("GPU 厂商探测结果: 不可靠");
                return;
            }

            var physical = result.Gpus.Where(x => !x.IsVirtual).Select(x => x.Name).ToArray();
            var virtualAdapters = result.Gpus.Where(x => x.IsVirtual).Select(x => x.Name).ToArray();

            log($"GPU 厂商探测结果: NVIDIA={result.HasNvidia} AMD={result.HasAmd} Intel={result.HasIntel}");

            if (physical.Length > 0)
                log($"物理 GPU: {string.Join(", ", physical)}");

            if (virtualAdapters.Length > 0)
                log($"虚拟显示适配器已忽略: {string.Join(", ", virtualAdapters)}");
        }

        private static GpuProbeResult ProbeGpuInfo(Action<string> log)
        {
            if (!OperatingSystem.IsWindows())
            {
                log("GPU 信息: 当前平台不是 Windows，跳过 WMI 探测");
                return new GpuProbeResult(false, false, false, false, Array.Empty<GpuInfo>());
            }

            return ProbeGpuInfoWindows(log);
        }

        [SupportedOSPlatform("windows")]
        private static GpuProbeResult ProbeGpuInfoWindows(Action<string> log)
        {
            try
            {
                var gpus = new List<GpuInfo>();
                var hasNvidia = false;
                var hasAmd = false;
                var hasIntel = false;

                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");

                foreach (ManagementObject item in searcher.Get())
                {
                    var name = Convert.ToString(item["Name"]) ?? "Unknown GPU";
                    var driver = Convert.ToString(item["DriverVersion"]) ?? "Unknown driver";
                    var ramText = item["AdapterRAM"] is null
                        ? "Unknown RAM"
                        : $"{Convert.ToUInt64(item["AdapterRAM"]) / 1024 / 1024}MB";

                    var isVirtual = IsVirtualDisplayAdapter(name);
                    gpus.Add(new GpuInfo(name, driver, ramText, isVirtual));
                    log($"GPU: {name}, driver={driver}, ram={ramText}, virtual={isVirtual}");

                    if (isVirtual)
                        continue;

                    var lowerName = name.ToLowerInvariant();
                    if (lowerName.Contains("nvidia"))
                        hasNvidia = true;
                    if (lowerName.Contains("amd") || lowerName.Contains("radeon"))
                        hasAmd = true;
                    if (lowerName.Contains("intel") || lowerName.Contains("iris") || lowerName.Contains("uhd graphics"))
                        hasIntel = true;
                }

                return new GpuProbeResult(true, hasNvidia, hasAmd, hasIntel, gpus);
            }
            catch (Exception ex)
            {
                log($"GPU 信息获取失败: {ex.Message}");
                return new GpuProbeResult(false, false, false, false, Array.Empty<GpuInfo>());
            }
        }

        private static bool IsVirtualDisplayAdapter(string name)
        {
            var lower = name.ToLowerInvariant();
            return lower.Contains("virtual") ||
                   lower.Contains("idd") ||
                   lower.Contains("todesk") ||
                   lower.Contains("oray") ||
                   lower.Contains("sunlogin") ||
                   lower.Contains("parsec") ||
                   lower.Contains("microsoft basic render") ||
                   lower.Contains("remote display");
        }

        private static void SafeDispose(IVideoEncoder? encoder)
        {
            try
            {
                encoder?.Dispose();
            }
            catch
            {
                // 初始化失败后的清理不能影响 fallback。
            }
        }
    }
}
