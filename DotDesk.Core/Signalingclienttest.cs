using System;
using System.Threading;
using System.Threading.Tasks;
using DotDesk.Core.Protocol;

namespace DotDesk.Core.Network
{
    /// <summary>
    /// 信令客户端连通性测试
    /// 在 Form 按钮事件里调用：await SignalingClientTest.RunAsync("ws://server:5000");
    /// </summary>
    public static class SignalingClientTest
    {
        public static async Task RunAsync(
            string serverUrl = "ws://localhost:5000",
            string deviceCode = "1234567890")
        {
            Console.WriteLine("=== 信令客户端测试 ===\n");

            // ── Host ──────────────────────────────────────────────────
            var host = new SignalingClient(serverUrl, deviceCode, "host");
            host.OnLog += msg => Console.WriteLine(msg);

            host.OnPeerJoined += () =>
            {
                Console.WriteLine("[Test] Host: Guest 上线，发送 Offer");
                host.SendOffer("v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\n（Offer SDP 占位）");
            };
            host.OnAnswer += sdp => Console.WriteLine($"[Test] Host: 收到 Answer ({sdp.Length} chars)");
            host.OnIceCandidate += ice => Console.WriteLine($"[Test] Host: 收到 ICE");
            host.OnPeerLeftGraceful += () => Console.WriteLine("[Test] Host: Guest 主动断开");
            host.OnPeerLeftAbnormal += () => Console.WriteLine("[Test] Host: Guest 意外掉线");

            // ── Guest ─────────────────────────────────────────────────
            var guest = new SignalingClient(serverUrl, deviceCode, "guest");
            guest.OnLog += msg => Console.WriteLine(msg);

            guest.OnPeerJoined += () => Console.WriteLine("[Test] Guest: Host 在线");
            guest.OnOffer += sdp =>
            {
                Console.WriteLine($"[Test] Guest: 收到 Offer ({sdp.Length} chars)，发送 Answer");
                guest.SendAnswer("v=0\r\no=- 1 1 IN IP4 127.0.0.1\r\n（Answer SDP 占位）");
                guest.SendIce(new IceCandidate
                {
                    Candidate = "candidate:1 1 UDP 2130706431 192.168.1.1 12345 typ host",
                    SdpMid = "0",
                    SdpMLineIndex = 0,
                });
            };
            guest.OnPeerLeftGraceful += () => Console.WriteLine("[Test] Guest: Host 主动断开");
            guest.OnPeerLeftAbnormal += () => Console.WriteLine("[Test] Guest: Host 意外掉线");

            // ── 连接 ──────────────────────────────────────────────────
            await host.ConnectAsync();
            await Task.Delay(300);
            await guest.ConnectAsync();

            // 等待交换
            await Task.Delay(2000);

            // ── 测试主动断开 ──────────────────────────────────────────
            Console.WriteLine("\n[Test] Guest 主动断开（发 bye）...");
            guest.Disconnect();
            await Task.Delay(500);

            // ── 清理 ──────────────────────────────────────────────────
            host.Dispose();
            guest.Dispose();

            Console.WriteLine("\n=== 测试完成 ===");
        }
    }
}