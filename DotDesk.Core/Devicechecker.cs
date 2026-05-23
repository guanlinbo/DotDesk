using System;
using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DotDesk.Core.Network
{
    /// <summary>
    /// 控制端连接前查询目标设备码是否在线
    /// </summary>

    public static class DeviceChecker
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        /// <summary>
        /// 查询设备码是否在线
        /// </summary>
        /// <param name="deviceCode">10位设备码（可带分隔符）</param>
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

    public record DeviceStatus(string Code, bool Online, string? Error, int? ServerLatencyMs)
    {
        public bool HasError => Error != null;

        public override string ToString() =>
            Online
                ? $"设备 {Code} 在线"
                : $"设备 {Code} 不在线{(Error != null ? $"（{Error}）" : "")}";
    }
}
