using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotDesk.Core.Protocol
{
    public enum DotDeskMessageType
    {
        Unknown,
        MouseMove,
        MouseDown,
        MouseUp,
        MouseWheel,
        KeyDown,
        KeyUp,
        KeyPress,
        CursorChanged,
        RequestKeyFrame,
        ClipboardText,
        FileTransferRequest,
        FileChunk,
        PermissionRequest,
        QualityChanged,
        MonitorChanged,
        SystemCommand,
        ConnectionStatus,
        SecureAttention,
        Ping,
        Pong
    }

    public sealed record DotDeskMessage(
        DotDeskMessageType MessageType,
        double X = 0,
        double Y = 0,
        int Button = 0,
        int Delta = 0,
        int KeyCode = 0,
        int ScanCode = 0,
        bool Extended = false,
        int CharCode = 0,
        int MonitorIndex = 0,
        int Quality = 0,
        long Offset = 0,
        int ChunkIndex = 0,
        int TotalChunks = 0,
        string? Text = null,
        string? Cursor = null,
        string? Command = null,
        string? Status = null,
        string? FileId = null,
        string? FileName = null,
        string? PayloadBase64 = null);

    public static class DotDeskMessageCodec
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private sealed class WireMessage
        {
            public string Type { get; set; } = "";
            public double X { get; set; }
            public double Y { get; set; }
            public int Button { get; set; }
            public int Delta { get; set; }
            public int KeyCode { get; set; }
            public int ScanCode { get; set; }
            public bool Extended { get; set; }
            public int CharCode { get; set; }
            public int MonitorIndex { get; set; }
            public int Quality { get; set; }
            public long Offset { get; set; }
            public int ChunkIndex { get; set; }
            public int TotalChunks { get; set; }
            public string? Text { get; set; }
            public string? Cursor { get; set; }
            public string? Command { get; set; }
            public string? Status { get; set; }
            public string? FileId { get; set; }
            public string? FileName { get; set; }
            public string? PayloadBase64 { get; set; }
        }

        public static string Serialize(DotDeskMessage message) =>
            JsonSerializer.Serialize(ToWire(message), JsonOptions);

        public static string MouseMove(double x, double y) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.MouseMove, X: x, Y: y));

        public static string MouseDown(int button) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.MouseDown, Button: button));

        public static string MouseUp(int button) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.MouseUp, Button: button));

        public static string MouseWheel(int delta) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.MouseWheel, Delta: delta));

        public static string KeyDown(int keyCode) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.KeyDown, KeyCode: keyCode));

        public static string KeyUp(int keyCode) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.KeyUp, KeyCode: keyCode));

        public static string KeyScan(bool down, int keyCode, int scanCode, bool extended) =>
            Serialize(new DotDeskMessage(
                down ? DotDeskMessageType.KeyDown : DotDeskMessageType.KeyUp,
                KeyCode: keyCode,
                ScanCode: scanCode,
                Extended: extended));

        public static string KeyPress(int charCode) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.KeyPress, CharCode: charCode));

        public static string CursorChanged(string cursor) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.CursorChanged, Cursor: cursor));

        public static string ClipboardText(string text) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.ClipboardText, Text: text));

        public static string SystemCommand(string command) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.SystemCommand, Command: command));

        public static string RequestKeyFrame() =>
            Serialize(new DotDeskMessage(DotDeskMessageType.RequestKeyFrame));

        public static string SecureAttention() =>
            Serialize(new DotDeskMessage(DotDeskMessageType.SecureAttention));

        public static string ConnectionStatus(string status, string? text = null) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.ConnectionStatus, Status: status, Text: text));

        public static string QualityChanged(int quality) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.QualityChanged, Quality: quality));

        public static string MonitorChanged(int monitorIndex) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.MonitorChanged, MonitorIndex: monitorIndex));

        public static string PermissionRequest(string command, string? text = null) =>
            Serialize(new DotDeskMessage(DotDeskMessageType.PermissionRequest, Command: command, Text: text));

        public static string FileTransferRequest(string fileId, string fileName, long offset = 0) =>
            Serialize(new DotDeskMessage(
                DotDeskMessageType.FileTransferRequest,
                Offset: offset,
                FileId: fileId,
                FileName: fileName));

        public static string FileChunk(
            string fileId,
            int chunkIndex,
            int totalChunks,
            string payloadBase64) =>
            Serialize(new DotDeskMessage(
                DotDeskMessageType.FileChunk,
                ChunkIndex: chunkIndex,
                TotalChunks: totalChunks,
                FileId: fileId,
                PayloadBase64: payloadBase64));

        public static string Ping() =>
            Serialize(new DotDeskMessage(DotDeskMessageType.Ping));

        public static string Pong() =>
            Serialize(new DotDeskMessage(DotDeskMessageType.Pong));

        public static DotDeskMessage Parse(string json)
        {
            var node = JsonNode.Parse(json);
            if (node == null)
                return new DotDeskMessage(DotDeskMessageType.Unknown);

            string type = node["type"]?.GetValue<string>() ?? "";
            return type switch
            {
                "mousemove" => new DotDeskMessage(
                    DotDeskMessageType.MouseMove,
                    X: node["x"]?.GetValue<double>() ?? 0,
                    Y: node["y"]?.GetValue<double>() ?? 0),

                "mousedown" => new DotDeskMessage(
                    DotDeskMessageType.MouseDown,
                    Button: node["button"]?.GetValue<int>() ?? 0),

                "mouseup" => new DotDeskMessage(
                    DotDeskMessageType.MouseUp,
                    Button: node["button"]?.GetValue<int>() ?? 0),

                "wheel" => new DotDeskMessage(
                    DotDeskMessageType.MouseWheel,
                    Delta: node["delta"]?.GetValue<int>() ?? 0),

                "keydown" => new DotDeskMessage(
                    DotDeskMessageType.KeyDown,
                    KeyCode: node["keyCode"]?.GetValue<int>() ?? 0,
                    ScanCode: node["scanCode"]?.GetValue<int>() ?? 0,
                    Extended: node["extended"]?.GetValue<bool>() ?? false),

                "keyup" => new DotDeskMessage(
                    DotDeskMessageType.KeyUp,
                    KeyCode: node["keyCode"]?.GetValue<int>() ?? 0,
                    ScanCode: node["scanCode"]?.GetValue<int>() ?? 0,
                    Extended: node["extended"]?.GetValue<bool>() ?? false),

                "keypress" => new DotDeskMessage(
                    DotDeskMessageType.KeyPress,
                    CharCode: node["charCode"]?.GetValue<int>() ?? 0),

                "cursor" => new DotDeskMessage(
                    DotDeskMessageType.CursorChanged,
                    Cursor: node["cursor"]?.GetValue<string>() ?? "arrow"),

                "requestKeyFrame" => new DotDeskMessage(DotDeskMessageType.RequestKeyFrame),
                "secureAttention" => new DotDeskMessage(DotDeskMessageType.SecureAttention),

                "clipboardText" => new DotDeskMessage(
                    DotDeskMessageType.ClipboardText,
                    Text: node["text"]?.GetValue<string>() ?? ""),

                "systemCommand" => new DotDeskMessage(
                    DotDeskMessageType.SystemCommand,
                    Command: node["command"]?.GetValue<string>() ?? ""),

                "connectionStatus" => new DotDeskMessage(
                    DotDeskMessageType.ConnectionStatus,
                    Status: node["status"]?.GetValue<string>() ?? "",
                    Text: node["text"]?.GetValue<string>()),

                "qualityChanged" => new DotDeskMessage(
                    DotDeskMessageType.QualityChanged,
                    Quality: node["quality"]?.GetValue<int>() ?? 0),

                "monitorChanged" => new DotDeskMessage(
                    DotDeskMessageType.MonitorChanged,
                    MonitorIndex: node["monitorIndex"]?.GetValue<int>() ?? 0),

                "permissionRequest" => new DotDeskMessage(
                    DotDeskMessageType.PermissionRequest,
                    Command: node["command"]?.GetValue<string>() ?? "",
                    Text: node["text"]?.GetValue<string>()),

                "fileTransferRequest" => new DotDeskMessage(
                    DotDeskMessageType.FileTransferRequest,
                    Offset: node["offset"]?.GetValue<long>() ?? 0,
                    FileId: node["fileId"]?.GetValue<string>() ?? "",
                    FileName: node["fileName"]?.GetValue<string>() ?? ""),

                "fileChunk" => new DotDeskMessage(
                    DotDeskMessageType.FileChunk,
                    ChunkIndex: node["chunkIndex"]?.GetValue<int>() ?? 0,
                    TotalChunks: node["totalChunks"]?.GetValue<int>() ?? 0,
                    FileId: node["fileId"]?.GetValue<string>() ?? "",
                    PayloadBase64: node["payloadBase64"]?.GetValue<string>() ?? ""),

                "ping" => new DotDeskMessage(DotDeskMessageType.Ping),
                "pong" => new DotDeskMessage(DotDeskMessageType.Pong),
                _ => new DotDeskMessage(DotDeskMessageType.Unknown)
            };
        }

        private static WireMessage ToWire(DotDeskMessage message) =>
            new()
            {
                Type = ToWireType(message.MessageType),
                X = message.X,
                Y = message.Y,
                Button = message.Button,
                Delta = message.Delta,
                KeyCode = message.KeyCode,
                ScanCode = message.ScanCode,
                Extended = message.Extended,
                CharCode = message.CharCode,
                MonitorIndex = message.MonitorIndex,
                Quality = message.Quality,
                Offset = message.Offset,
                ChunkIndex = message.ChunkIndex,
                TotalChunks = message.TotalChunks,
                Text = message.Text,
                Cursor = message.Cursor,
                Command = message.Command,
                Status = message.Status,
                FileId = message.FileId,
                FileName = message.FileName,
                PayloadBase64 = message.PayloadBase64
            };

        private static string ToWireType(DotDeskMessageType type) =>
            type switch
            {
                DotDeskMessageType.MouseMove => "mousemove",
                DotDeskMessageType.MouseDown => "mousedown",
                DotDeskMessageType.MouseUp => "mouseup",
                DotDeskMessageType.MouseWheel => "wheel",
                DotDeskMessageType.KeyDown => "keydown",
                DotDeskMessageType.KeyUp => "keyup",
                DotDeskMessageType.KeyPress => "keypress",
                DotDeskMessageType.CursorChanged => "cursor",
                DotDeskMessageType.RequestKeyFrame => "requestKeyFrame",
                DotDeskMessageType.ClipboardText => "clipboardText",
                DotDeskMessageType.FileTransferRequest => "fileTransferRequest",
                DotDeskMessageType.FileChunk => "fileChunk",
                DotDeskMessageType.PermissionRequest => "permissionRequest",
                DotDeskMessageType.QualityChanged => "qualityChanged",
                DotDeskMessageType.MonitorChanged => "monitorChanged",
                DotDeskMessageType.SystemCommand => "systemCommand",
                DotDeskMessageType.ConnectionStatus => "connectionStatus",
                DotDeskMessageType.SecureAttention => "secureAttention",
                DotDeskMessageType.Ping => "ping",
                DotDeskMessageType.Pong => "pong",
                _ => "unknown"
            };
    }
}
