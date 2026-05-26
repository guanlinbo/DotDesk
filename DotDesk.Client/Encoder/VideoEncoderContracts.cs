using System;
using System.Collections.Generic;

namespace DotDesk.Client.Encoder
{
    public enum VideoPixelFormat
    {
        Bgra,
        Nv12
    }

    public enum VideoCodecFormat
    {
        H264,
        H265
    }

    public enum VideoConnectionMode
    {
        P2P,
        Relay
    }

    public enum EncodedFrameType
    {
        Unknown,
        I,
        P
    }

    public enum HardwareVideoEncoderKind
    {
        Nvenc,
        Amf,
        Qsv
    }

    public sealed record VideoEncoderOptions(
        int Width,
        int Height,
        VideoPixelFormat InputPixelFormat,
        int TargetFps,
        int TargetBitrate,
        int GopSize,
        bool LowLatencyMode,
        VideoConnectionMode ConnectionMode,
        VideoCodecFormat CodecFormat = VideoCodecFormat.H264,
        int QualityLevel = 5);

    public sealed record VideoEncoderUpdateOptions(
        int? TargetFps = null,
        int? TargetBitrate = null,
        int? GopSize = null,
        int? QualityLevel = null,
        VideoConnectionMode? ConnectionMode = null,
        bool? LowLatencyMode = null);

    public sealed record VideoEncoderInfo(
        string Name,
        bool IsHardware,
        VideoCodecFormat CodecFormat,
        int Width,
        int Height,
        int CurrentFps,
        int CurrentBitrate,
        int CurrentGopSize,
        int MinKeyFrameInterval,
        VideoPixelFormat InputPixelFormat,
        VideoConnectionMode ConnectionMode);

    public sealed record EncodedVideoPacket(
        byte[] Data,
        bool IsKeyFrame,
        EncodedFrameType FrameType,
        long PresentationTimeMs,
        long EncodeElapsedMs,
        int EncodedSize,
        string KeyFrameReason = "");

    public sealed record VideoEncodeResult(
        IReadOnlyList<EncodedVideoPacket> Packets,
        long EncodeElapsedMs,
        int EncodedBytes,
        bool HasKeyFrame);

    public interface IVideoEncoder : IDisposable
    {
        event Action<EncodedVideoPacket>? OnEncoded;

        VideoEncoderInfo Info { get; }
        string Name { get; }
        bool IsHardware { get; }

        VideoEncodeResult Encode(ReadOnlyMemory<byte> frame, long timestampMs = 0);
        void UpdateOptions(VideoEncoderUpdateOptions options);
        void ForceIdr(string reason);
        void Flush();
        void Close();
    }

    public interface IHardwareVideoEncoder : IVideoEncoder
    {
        HardwareVideoEncoderKind HardwareKind { get; }
    }
}
