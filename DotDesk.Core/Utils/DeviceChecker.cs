using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DotDesk.Core.Utils
{
    /// <summary>
    /// 网络与设备在线状态检测。
    /// </summary>
    public static class DeviceChecker
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        /// <summary>
        /// 启动时先检测服务器端口是否可达，避免信令没连上却显示“等待控制端连接”。
        /// </summary>
        public static async Task<ServerStatus> CheckServerAsync(
            string serverUrl,
            CancellationToken ct = default)
        {
            try
            {
                var uri = new Uri(serverUrl);
                int port = uri.Port > 0
                    ? uri.Port
                    : uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;

                var sw = Stopwatch.StartNew();
                using var client = new TcpClient();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

                await client.ConnectAsync(uri.Host, port, timeoutCts.Token).ConfigureAwait(false);
                sw.Stop();

                return new ServerStatus(true, null, (int)Math.Max(1, sw.ElapsedMilliseconds));
            }
            catch (OperationCanceledException)
            {
                return new ServerStatus(false, "服务器连接超时", null);
            }
            catch (SocketException ex)
            {
                return new ServerStatus(false, $"服务器不可达: {ex.Message}", null);
            }
            catch (Exception ex)
            {
                return new ServerStatus(false, $"网络检测失败: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 查询指定设备码是否在线。
        /// </summary>
        public static async Task<DeviceStatus> CheckAsync(
            string serverUrl,
            string deviceCode,
            CancellationToken ct = default)
        {
            var code = deviceCode.Replace("-", "").Replace(" ", "").Trim();
            var url = $"{serverUrl.TrimEnd('/')}/api/device/{code}";

            try
            {
                var sw = Stopwatch.StartNew();
                var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                sw.Stop();

                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var doc = JsonDocument.Parse(body);
                bool online = doc.RootElement.GetProperty("online").GetBoolean();

                return new DeviceStatus(code, online, null, (int)Math.Max(1, sw.ElapsedMilliseconds));
            }
            catch (TaskCanceledException)
            {
                return new DeviceStatus(code, false, "请求超时", null);
            }
            catch (HttpRequestException ex)
            {
                return new DeviceStatus(code, false, $"网络错误: {ex.Message}", null);
            }
            catch (Exception ex)
            {
                return new DeviceStatus(code, false, $"错误: {ex.Message}", null);
            }
        }
    }

    public record ServerStatus(bool Reachable, string? Error, int? ServerLatencyMs)
    {
        public bool HasError => Error != null;
    }

    public record DeviceStatus(string Code, bool Online, string? Error, int? ServerLatencyMs)
    {
        public bool HasError => Error != null;

        public override string ToString() =>
            Online
                ? $"设备 {Code} 在线"
                : $"设备 {Code} 不在线{(Error != null ? $"（{Error}）" : "")}";
    }
}
