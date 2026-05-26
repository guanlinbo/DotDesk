// LibDataChannel.cs
// P/Invoke 封装 libdatachannel 的 C API（来自 DataChannelDotnet 包）。
// 只封装 DotDesk 实际使用的接口：PeerConnection、DataChannel、Video Track（H264）。
// 依赖 NuGet: DataChannelDotnet

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DotDesk.Core.WebRtc
{
    // ─── 常量 ──────────────────────────────────────────────────────────────────

    public static class RtcConst
    {
        public const int RTC_ERR_SUCCESS = 0;
        public const int RTC_ERR_INVALID = -1;
        public const int RTC_ERR_FAILURE = -2;
        public const int RTC_ERR_NOT_AVAIL = -3;
        public const int RTC_ERR_TOO_SMALL = -4;

        // NAL separator: Annex-B 长起始码 00 00 00 01
        public const int RTC_NAL_SEPARATOR_LONG_START_SEQUENCE = 1;

        // H264 RTP 时钟率
        public const uint H264_CLOCK_RATE = 90_000;

        // 默认 MTU（libdatachannel 内部会用此值做 FU-A 分片）
        public const int DEFAULT_MAX_FRAGMENT_SIZE = 1100;
    }

    // ─── 枚举 ──────────────────────────────────────────────────────────────────

    public enum RtcState
    {
        New = 0, Connecting, Connected, Disconnected, Failed, Closed
    }

    public enum RtcGatheringState
    {
        New = 0, InProgress, Complete
    }

    public enum RtcSignalingState
    {
        Stable = 0, HaveLocalOffer, HaveRemoteOffer,
        HaveLocalPranswer, HaveRemotePranswer
    }

    public enum RtcDirection
    {
        Unknown = 0, SendOnly, RecvOnly, SendRecv, Inactive
    }

    // ─── 回调委托 ──────────────────────────────────────────────────────────────

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DescriptionCallback(int pc, [MarshalAs(UnmanagedType.LPStr)] string sdp,
        [MarshalAs(UnmanagedType.LPStr)] string type, IntPtr ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void CandidateCallback(int pc, [MarshalAs(UnmanagedType.LPStr)] string cand,
        [MarshalAs(UnmanagedType.LPStr)] string mid, IntPtr ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void StateChangeCallback(int pc, RtcState state, IntPtr ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void GatheringStateCallback(int pc, RtcGatheringState state, IntPtr ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DataChannelCallback(int pc, int dc, IntPtr ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void TrackCallback(int pc, int tr, IntPtr ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OpenCallback(int id, IntPtr ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ClosedCallback(int id, IntPtr ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ErrorCallback(int id, [MarshalAs(UnmanagedType.LPStr)] string error, IntPtr ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MessageCallback(int id, IntPtr message, int size, IntPtr ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void LogCallback(int level, IntPtr message);

    // ─── 配置结构体 ────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct RtcIceServer
    {
        [MarshalAs(UnmanagedType.LPStr)] public string Hostname;
        public int Port;
        [MarshalAs(UnmanagedType.LPStr)] public string? Username;
        [MarshalAs(UnmanagedType.LPStr)] public string? Password;
        public int Type; // 0=None/STUN, 1=TURN/UDP, 2=TURN/TCP, 3=TURN/TLS
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RtcConfiguration
    {
        public IntPtr IceServers;      // char** ice server URL list
        public int IceServersCount;
        [MarshalAs(UnmanagedType.LPStr)] public string? ProxyServer;
        [MarshalAs(UnmanagedType.LPStr)] public string? BindAddress;
        public int CertificateType; // 0=Default, 1=ECDSA, 2=RSA
        public int IceTransportPolicy; // 0=All, 1=Relay
        public byte EnableIceTcp;    // 1=enable TCP candidates
        public byte EnableIceUdpMux;
        public byte DisableAutoNegotiation;
        public byte ForceMediaTransport;
        public ushort PortRangeBegin;
        public ushort PortRangeEnd;
        public int Mtu;             // 0=auto
        public int MaxMessageSize;  // 0=auto
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RtcTrackInit
    {
        public RtcDirection Direction;
        [MarshalAs(UnmanagedType.LPStr)] public string Codec; // "H264"
        public int PayloadType;     // 96
        public uint Ssrc;
        [MarshalAs(UnmanagedType.LPStr)] public string Mid;
        [MarshalAs(UnmanagedType.LPStr)] public string? Name;
        [MarshalAs(UnmanagedType.LPStr)] public string? MsId;
        [MarshalAs(UnmanagedType.LPStr)] public string? TrackId;
        [MarshalAs(UnmanagedType.LPStr)] public string? Profile; // "baseline"
        [MarshalAs(UnmanagedType.LPStr)] public string? Level;   // "31"
    }

    // H264 打包配置
    //[StructLayout(LayoutKind.Sequential)]
    // 与 libdatachannel rtcPacketizerInit 完全对齐（Pack=1 防止 C# 自动插入 padding）
    // 字段顺序严格对应 rtc.h 第347-374行
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RtcPacketizationHandlerInit
    {
        public uint Ssrc;            // uint32_t ssrc
        public IntPtr Cname;           // const char* cname（需手动 AllocHGlobal）
        public byte PayloadType;     // uint8_t payloadType
        public uint ClockRate;       // uint32_t clockRate（90000 for H264）
        public ushort SequenceNumber;  // uint16_t sequenceNumber
        public uint Timestamp;       // uint32_t timestamp
        public ushort MaxFragmentSize; // uint16_t maxFragmentSize（0=default≈1200B）
        public byte _pad1;           // compiler padding in C struct （nalSeparator 之前）
        public int NalSeparator;    // rtcNalUnitSeparator (int32)
        // 以下字段 H264 用不到，全部置零即可
        public int ObuPacketization;
        public byte PlayoutDelayId;
        public ushort PlayoutDelayMin;
        public ushort PlayoutDelayMax;
        public byte ColorSpaceId;
        public byte ColorChromaSitingHorz;
        public byte ColorChromaSitingVert;
        public byte ColorRange;
        public byte ColorPrimaries;
        public byte ColorTransfer;
        public byte ColorMatrix;
    }

    // ─── P/Invoke ──────────────────────────────────────────────────────────────
    // DataChannelDotnet 会自动 load datachannel.dll（Windows）或 libdatachannel.so（Linux）

    public static class Rtc
    {
        private static int _nativeResolverInstalled;
        private static readonly object LogLock = new();
        private static LogCallback? _logCallback;
        private static Action<string>? _logSink;

        // 通过 DataChannelDotnet.Bindings 提供的 Rtc 类转发，避免重复 DllImport
        // DataChannelDotnet 已经处理了 DLL 加载路径

        // — 全局 —
        public static void InitLogger(int level, Action<string>? logSink = null)
        {
            EnsureNativeResolver();
            lock (LogLock)
            {
                _logSink = logSink;
                if (logSink == null)
                {
                    NativeMethods.rtcInitLogger(level, null);
                    return;
                }

                _logCallback ??= OnNativeLog;
                NativeMethods.rtcInitLogger(level, _logCallback);
            }
        }

        private static void OnNativeLog(int level, IntPtr message)
        {
            try
            {
                string text = Marshal.PtrToStringUTF8(message) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text)) return;

                Action<string>? sink;
                lock (LogLock) sink = _logSink;
                sink?.Invoke($"level={level} {text}");
            }
            catch
            {
                // Native callbacks must never throw back into libdatachannel.
            }
        }

        // — PeerConnection —
        public static int CreatePeerConnection(string[] iceServers, bool enableIceTcp = true, int mtu = 1300,
            bool relayOnly = false, bool disableAutoNegotiation = false)
        {
            EnsureNativeResolver();
            iceServers ??= Array.Empty<string>();

            IntPtr iceServerArray = IntPtr.Zero;
            var iceServerPtrs = new IntPtr[iceServers.Length];
            try
            {
                if (iceServers.Length > 0)
                {
                    iceServerArray = Marshal.AllocHGlobal(IntPtr.Size * iceServers.Length);
                    for (int i = 0; i < iceServers.Length; i++)
                    {
                        iceServerPtrs[i] = Marshal.StringToHGlobalAnsi(iceServers[i]);
                        Marshal.WriteIntPtr(iceServerArray, i * IntPtr.Size, iceServerPtrs[i]);
                    }
                }

                var config = new RtcConfiguration
                {
                    IceServers = iceServerArray,
                    IceServersCount = iceServers.Length,
                    IceTransportPolicy = relayOnly ? 1 : 0,
                    EnableIceTcp = enableIceTcp ? (byte)1 : (byte)0,
                    DisableAutoNegotiation = disableAutoNegotiation ? (byte)1 : (byte)0,
                    Mtu = mtu,
                };

                return CreatePeerConnection(ref config);
            }
            finally
            {
                foreach (IntPtr ptr in iceServerPtrs)
                {
                    if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
                }
                if (iceServerArray != IntPtr.Zero) Marshal.FreeHGlobal(iceServerArray);
            }
        }

        public static int CreatePeerConnection(ref RtcConfiguration config)
        {
            EnsureNativeResolver();
            // 手动 marshal RtcConfiguration → rtcConfiguration
            // DataChannelDotnet 的绑定结构体名称略有差异，直接用原生 C API
            IntPtr configPtr = Marshal.AllocHGlobal(Marshal.SizeOf<RtcConfiguration>());
            try
            {
                Marshal.StructureToPtr(config, configPtr, false);
                return NativeMethods.rtcCreatePeerConnection(configPtr);
            }
            finally
            {
                Marshal.DestroyStructure<RtcConfiguration>(configPtr);
                Marshal.FreeHGlobal(configPtr);
            }
        }

        private static void EnsureNativeResolver()
        {
            if (Interlocked.Exchange(ref _nativeResolverInstalled, 1) == 1) return;
            NativeLibrary.SetDllImportResolver(typeof(Rtc).Assembly, ResolveNativeLibrary);
        }

        private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, "datachannel", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            foreach (string nativePath in EnumerateNativeLibraryCandidates())
            {
                if (!File.Exists(nativePath)) continue;
                string? nativeDirectory = Path.GetDirectoryName(nativePath);
                if (!string.IsNullOrWhiteSpace(nativeDirectory))
                    NativeSearchPath.SetDllDirectory(nativeDirectory);
                return NativeLibrary.Load(nativePath);
            }

            return IntPtr.Zero;
        }

        private static string[] EnumerateNativeLibraryCandidates()
        {
            string arch = RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "win-x86" : "win-x64";
            string baseDir = AppContext.BaseDirectory;
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string packageNativeDir = Path.Combine(userProfile, ".nuget", "packages", "datachanneldotnet", "1.3.1",
                "runtimes", arch, "native");

            return new[]
            {
                Path.Combine(baseDir, "datachannel.dll"),
                Path.Combine(baseDir, "runtimes", arch, "native", "datachannel.dll"),
                Path.Combine(packageNativeDir, "datachannel.dll"),
            };
        }

        public static void DeletePeerConnection(int pc) => NativeMethods.rtcDeletePeerConnection(pc);
        public static void SetPeerConnectionUserPointer(int pc, IntPtr ptr) =>
            NativeMethods.rtcSetUserPointer(pc, ptr);

        public static void SetLocalDescriptionCallback(int pc, DescriptionCallback cb) =>
            NativeMethods.rtcSetLocalDescriptionCallback(pc, cb, IntPtr.Zero);
        public static void SetLocalCandidateCallback(int pc, CandidateCallback cb) =>
            NativeMethods.rtcSetLocalCandidateCallback(pc, cb, IntPtr.Zero);
        public static void SetStateChangeCallback(int pc, StateChangeCallback cb) =>
            NativeMethods.rtcSetStateChangeCallback(pc, cb, IntPtr.Zero);
        public static void SetGatheringStateChangeCallback(int pc, GatheringStateCallback cb) =>
            NativeMethods.rtcSetGatheringStateChangeCallback(pc, cb, IntPtr.Zero);
        public static void SetDataChannelCallback(int pc, DataChannelCallback cb) =>
            NativeMethods.rtcSetDataChannelCallback(pc, cb, IntPtr.Zero);
        public static void SetTrackCallback(int pc, TrackCallback cb) =>
            NativeMethods.rtcSetTrackCallback(pc, cb, IntPtr.Zero);

        public static int SetRemoteDescription(int pc, string sdp, string type) =>
            NativeMethods.rtcSetRemoteDescription(pc, sdp, type);
        public static int SetLocalDescription(int pc, string? type = null) =>
            NativeMethods.rtcSetLocalDescription(pc, type);
        public static int AddRemoteCandidate(int pc, string cand, string mid) =>
            NativeMethods.rtcAddRemoteCandidate(pc, cand, mid);

        public static string? GetLocalDescription(int pc)
        {
            var buf = new byte[8192];
            int ret = NativeMethods.rtcGetLocalDescription(pc, buf, buf.Length);
            return ret > 0 ? Encoding.UTF8.GetString(buf, 0, ret - 1) : null;
        }

        public static string? GetLocalDescriptionType(int pc)
        {
            var buf = new byte[32];
            int ret = NativeMethods.rtcGetLocalDescriptionType(pc, buf, buf.Length);
            return ret > 0 ? Encoding.UTF8.GetString(buf, 0, ret - 1) : null;
        }

        // — DataChannel —
        public static int CreateDataChannel(int pc, string label) =>
            NativeMethods.rtcCreateDataChannel(pc, label);
        public static void DeleteDataChannel(int dc) => NativeMethods.rtcDeleteDataChannel(dc);
        public static void SetOpenCallback(int id, OpenCallback cb) =>
            NativeMethods.rtcSetOpenCallback(id, cb, IntPtr.Zero);
        public static void SetClosedCallback(int id, ClosedCallback cb) =>
            NativeMethods.rtcSetClosedCallback(id, cb, IntPtr.Zero);
        public static void SetErrorCallback(int id, ErrorCallback cb) =>
            NativeMethods.rtcSetErrorCallback(id, cb, IntPtr.Zero);
        public static void SetMessageCallback(int id, MessageCallback cb) =>
            NativeMethods.rtcSetMessageCallback(id, cb, IntPtr.Zero);
        public static int SendMessage(int id, ReadOnlySpan<byte> data)
        {
            // GCHandle 方式替代 unsafe/fixed，不需要 AllowUnsafeBlocks
            byte[] arr = data.ToArray();
            var gch = GCHandle.Alloc(arr, GCHandleType.Pinned);
            try { return NativeMethods.rtcSendMessage(id, gch.AddrOfPinnedObject(), arr.Length); }
            finally { gch.Free(); }
        }
        public static int SendMessage(int id, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text + "\0");
            var gch = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return NativeMethods.rtcSendMessage(id, gch.AddrOfPinnedObject(), -1); } // -1=string
            finally { gch.Free(); }
        }

        // — Video Track —
        public static int AddTrack(int pc, ref RtcTrackInit init)
        {
            IntPtr initPtr = Marshal.AllocHGlobal(Marshal.SizeOf<RtcTrackInit>());
            try
            {
                Marshal.StructureToPtr(init, initPtr, false);
                return NativeMethods.rtcAddTrackEx(pc, initPtr);
            }
            finally
            {
                Marshal.DestroyStructure<RtcTrackInit>(initPtr);
                Marshal.FreeHGlobal(initPtr);
            }
        }
        public static int AddTrack(int pc, string mediaDescriptionSdp) =>
            NativeMethods.rtcAddTrack(pc, mediaDescriptionSdp);
        public static void DeleteTrack(int tr) => NativeMethods.rtcDeleteTrack(tr);

        /// <summary>
        /// SetH264Depacketizer 在 datachannel.dll 1.3.1 中不存在，使用应用层 H264RtpDepacketizer 替代。
        /// 启用后 Track 的 OnMessage 回调给出已重组的完整 Annex-B NAL，
        /// <summary>查询 track/channel 当前 native 发送缓冲大小（字节）。负数表示错误。</summary>
        public static int GetBufferedAmount(int id)
        {
            try { return NativeMethods.rtcGetBufferedAmount(id); }
            catch { return -1; }
        }

        public static int SetH264Packetizer(int tr, ref RtcPacketizationHandlerInit init)
        {
            // Cname 是 const char*，需要手动分配并在调用后释放
            // init.Cname 调用前由调用方设置为 Marshal.StringToHGlobalAnsi(cnameStr)
            IntPtr initPtr = Marshal.AllocHGlobal(Marshal.SizeOf<RtcPacketizationHandlerInit>());
            try
            {
                Marshal.StructureToPtr(init, initPtr, false);
                int ret = NativeMethods.rtcSetH264Packetizer(tr, initPtr);
                return ret;
            }
            finally
            {
                Marshal.DestroyStructure<RtcPacketizationHandlerInit>(initPtr);
                Marshal.FreeHGlobal(initPtr);
            }
        }

        /// <summary>
        /// 便捷方法：自动分配/释放 cname 字符串，MaxFragmentSize=1100（安全 MTU）
        /// </summary>
        public static int SetH264Packetizer(int tr, uint ssrc, string cname,
            byte payloadType = 96, uint clockRate = 90_000,
            ushort maxFragmentSize = 1100)
        {
            IntPtr cnamePtr = Marshal.StringToHGlobalAnsi(cname);
            try
            {
                var init = new RtcPacketizationHandlerInit
                {
                    Ssrc = ssrc,
                    Cname = cnamePtr,
                    PayloadType = payloadType,
                    ClockRate = clockRate,
                    SequenceNumber = (ushort)new Random().Next(0, 65535),
                    Timestamp = (uint)new Random().Next(),
                    MaxFragmentSize = maxFragmentSize,      // ≤ MTU，libjuice buffer 安全
                    NalSeparator = 1,  // RTC_NAL_SEPARATOR_LONG_START_SEQUENCE (0x00000001)
                };
                return SetH264Packetizer(tr, ref init);
            }
            finally
            {
                Marshal.FreeHGlobal(cnamePtr);
            }
        }

        // 发送一帧 H264 NAL（Annex-B 格式）
        public static int SendFrame(int tr, ReadOnlySpan<byte> nal, ulong timestampUs)
        {
            // rtcSendMessage 发送二进制帧到 Video Track；timestamp 由 packetizer 内部维护。
            byte[] arr = nal.ToArray();
            var gch = GCHandle.Alloc(arr, GCHandleType.Pinned);
            try { return NativeMethods.rtcSendMessage(tr, gch.AddrOfPinnedObject(), arr.Length); }
            finally { gch.Free(); }
        }

        // 获取 Track 上收到的原始消息（Receiver 端）
        public static void SetTrackOpenCallback(int tr, OpenCallback cb) =>
            NativeMethods.rtcSetOpenCallback(tr, cb, IntPtr.Zero);
        public static void SetTrackClosedCallback(int tr, ClosedCallback cb) =>
            NativeMethods.rtcSetClosedCallback(tr, cb, IntPtr.Zero);
        public static void SetTrackMessageCallback(int tr, MessageCallback cb) =>
            NativeMethods.rtcSetMessageCallback(tr, cb, IntPtr.Zero);
    }

    // ─── Native method 声明（从 DataChannelDotnet 的 Bindings 层 re-export）──

    internal static class NativeMethods
    {
        // DataChannelDotnet 包把 datachannel.dll 放在正确的 runtimes/ 目录下
        // 包名: DataChannelDotnet.Bindings
        private const string Lib = "datachannel";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void rtcInitLogger(int level, LogCallback? callback);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcCreatePeerConnection(IntPtr config);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcDeletePeerConnection(int pc);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetUserPointer(int id, IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetLocalDescriptionCallback(int pc, DescriptionCallback? cb, IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetLocalCandidateCallback(int pc, CandidateCallback? cb, IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetStateChangeCallback(int pc, StateChangeCallback? cb, IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetGatheringStateChangeCallback(int pc, GatheringStateCallback? cb, IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetDataChannelCallback(int pc, DataChannelCallback? cb, IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetTrackCallback(int pc, TrackCallback? cb, IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetRemoteDescription(int pc,
            [MarshalAs(UnmanagedType.LPStr)] string sdp,
            [MarshalAs(UnmanagedType.LPStr)] string type);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetLocalDescription(int pc,
            [MarshalAs(UnmanagedType.LPStr)] string? type);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcAddRemoteCandidate(int pc,
            [MarshalAs(UnmanagedType.LPStr)] string cand,
            [MarshalAs(UnmanagedType.LPStr)] string mid);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcGetLocalDescription(int pc, byte[] buffer, int size);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcGetLocalDescriptionType(int pc, byte[] buffer, int size);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcCreateDataChannel(int pc,
            [MarshalAs(UnmanagedType.LPStr)] string label);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcDeleteDataChannel(int dc);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetOpenCallback(int id, OpenCallback? cb, IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetClosedCallback(int id, ClosedCallback? cb, IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetErrorCallback(int id, ErrorCallback? cb, IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetMessageCallback(int id, MessageCallback? cb, IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSendMessage(int id, IntPtr data, int size);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcAddTrackEx(int pc, IntPtr init);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcAddTrack(int pc,
            [MarshalAs(UnmanagedType.LPStr)] string mediaDescriptionSdp);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcSetH264Packetizer(int tr, IntPtr init);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcGetBufferedAmount(int id);  // 当前 native 发送缓冲字节数

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rtcDeleteTrack(int tr);
    }

    internal static class NativeSearchPath
    {
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectoryW(string? lpPathName);

        public static void SetDllDirectory(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                _ = SetDllDirectoryW(path);
        }
    }
}