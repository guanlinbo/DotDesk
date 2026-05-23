using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotDesk.Core.Protocol
{
    // ── 消息类型常量 ──────────────────────────────────────────────────
    public static class MsgType
    {
        public const string Offer = "offer";
        public const string Answer = "answer";
        public const string Ice = "ice";
        public const string Bye = "bye";
        public const string PeerJoined = "peer-joined";
        public const string PeerLeft = "peer-left";
        public const string Auth = "auth";        // 控制端发送密码
        public const string AuthResult = "auth-result"; // 被控端回复验证结果
    }

    // ── 统一消息体 ────────────────────────────────────────────────────
    public class SignalingMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        // offer / answer SDP
        [JsonPropertyName("sdp")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sdp { get; set; }

        // ICE candidate
        [JsonPropertyName("candidate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IceCandidate? Candidate { get; set; }

        // peer-joined 时携带的角色
        [JsonPropertyName("role")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Role { get; set; }

        // peer-left 时：true = 主动断开，false = 意外掉线
        [JsonPropertyName("graceful")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Graceful { get; set; }

        // auth：控制端发送的密码
        [JsonPropertyName("password")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Password { get; set; }

        // auth-result：验证是否通过
        [JsonPropertyName("authOk")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? AuthOk { get; set; }

        // auth-result：被控端电脑名，用于控制端标题栏显示
        [JsonPropertyName("deviceName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DeviceName { get; set; }

        // ── 序列化 ────────────────────────────────────────────────────
        public string ToJson() =>
            JsonSerializer.Serialize(this, JsonOpts.Default);

        public static SignalingMessage? FromJson(string json) =>
            JsonSerializer.Deserialize<SignalingMessage>(json, JsonOpts.Default);

        // ── 工厂方法 ──────────────────────────────────────────────────
        public static SignalingMessage Offer(string sdp) => new() { Type = MsgType.Offer, Sdp = sdp };
        public static SignalingMessage Answer(string sdp) => new() { Type = MsgType.Answer, Sdp = sdp };
        public static SignalingMessage Ice(IceCandidate c) => new() { Type = MsgType.Ice, Candidate = c };
        public static SignalingMessage Bye() => new() { Type = MsgType.Bye };
        public static SignalingMessage Auth(string password) => new() { Type = MsgType.Auth, Password = password };
        public static SignalingMessage AuthResult(bool ok, string? deviceName = null) =>
            new() { Type = MsgType.AuthResult, AuthOk = ok, DeviceName = deviceName };
    }

    // ── ICE Candidate ─────────────────────────────────────────────────
    public class IceCandidate
    {
        [JsonPropertyName("candidate")]
        public string Candidate { get; set; } = "";

        [JsonPropertyName("sdpMid")]
        public string? SdpMid { get; set; }

        [JsonPropertyName("sdpMLineIndex")]
        public int? SdpMLineIndex { get; set; }
    }

    internal static class JsonOpts
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }
}
