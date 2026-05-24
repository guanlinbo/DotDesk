using System;
using System.Threading;
using System.Threading.Tasks;
using DotDesk.Core.Logging;
using DotDesk.Core.Protocol;
using WebSocketSharp;

namespace DotDesk.Core.Network
{
    public enum SignalingState
    {
        Disconnected,
        Connecting,
        Connected,
        Paired,
        Reconnecting,
    }

    public sealed class SignalingClient : IDisposable
    {
        public event Action<SignalingState>? OnStateChanged;
        public event Action? OnPeerJoined;
        public event Action? OnPeerLeftGraceful;
        public event Action? OnPeerLeftAbnormal;
        public event Action<string>? OnOffer;
        public event Action<string>? OnAnswer;
        public event Action<IceCandidate>? OnIceCandidate;
        public event Action<string>? OnAuth;
        public event Action<bool>? OnAuthResult;
        public event Action<bool, string?>? OnAuthResultInfo;
        public event Action<string>? OnLog;

        public bool IsConnected => _ws?.ReadyState == WebSocketState.Open;
        public SignalingState State { get; private set; } = SignalingState.Disconnected;

        public string DeviceCode { get; }
        public string Role { get; }

        public bool AutoReconnect { get; set; } = true;
        public int ReconnectInitialSecs { get; set; } = 3;
        public int ReconnectMaxSecs { get; set; } = 30;

        private readonly string _serverUrl;
        private WebSocket? _ws;

        private bool _disposed;
        private bool _manualDisconnect;

        private CancellationTokenSource _reconnectCts = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        private int _reconnectLoopRunning;

        public SignalingClient(string serverUrl, string deviceCode, string role)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            DeviceCode = deviceCode.Replace("-", "").Replace(" ", "").Trim();
            Role = role;
        }

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            if (_disposed) return;

            await _connectLock.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                if (_disposed) return;
                if (_ws?.ReadyState == WebSocketState.Open) return;

                _manualDisconnect = false;

                SetState(SignalingState.Connecting);

                string url = $"{_serverUrl}/ws/{DeviceCode}/{Role}";
                Log($"连接中  {url}");

                try
                {
                    _ws?.Close();
                    _ws = null;
                }
                catch { }

                var ws = new WebSocket(url);
                _ws = ws;

                ws.OnOpen += (_, _) =>
                {
                    Log($"已连接  role={Role}  code={DeviceCode}");
                    SetState(SignalingState.Connected);
                };

                ws.OnMessage += (_, e) =>
                {
                    if (!_disposed)
                        HandleMessage(e.Data);
                };

                ws.OnError += (_, e) =>
                {
                    Log($"WebSocket 错误: {e.Message}");
                };

                ws.OnClose += (_, e) =>
                {
                    Log($"连接断开  code={e.Code}  reason={e.Reason}");
                    SetState(SignalingState.Disconnected);

                    bool graceful =
                        _manualDisconnect ||
                        e.Code == 1000 ||
                        e.Code == 1001 ||
                        e.Code == 4000;

                    if (AutoReconnect && !_disposed && !graceful)
                    {
                        StartReconnectLoop();
                    }
                };

                await Task.Run(() => ws.Connect(), ct).ConfigureAwait(false);
            }
            finally
            {
                _connectLock.Release();
            }
        }

        public void Disconnect()
        {
            if (_disposed) return;

            AutoReconnect = false;
            _manualDisconnect = true;

            try { _reconnectCts.Cancel(); } catch { }

            TrySend(SignalingMessage.Bye());

            Task.Run(async () =>
            {
                await Task.Delay(100);
                try { _ws?.Close(); } catch { }
            });
        }

        public void SendBye() => TrySend(SignalingMessage.Bye());
        public void SendOffer(string sdp) => TrySend(SignalingMessage.Offer(sdp));
        public void SendAuth(string password) => TrySend(SignalingMessage.Auth(password));
        public void SendAuthResult(bool ok, string? deviceName = null) => TrySend(SignalingMessage.AuthResult(ok, deviceName));
        public void SendAnswer(string sdp) => TrySend(SignalingMessage.Answer(sdp));
        public void SendIce(IceCandidate c) => TrySend(SignalingMessage.Ice(c));

        private void StartReconnectLoop()
        {
            if (Interlocked.Exchange(ref _reconnectLoopRunning, 1) == 1)
                return;

            SetState(SignalingState.Reconnecting);

            _ = Task.Run(async () =>
            {
                try
                {
                    await ReconnectLoopAsync();
                }
                finally
                {
                    Interlocked.Exchange(ref _reconnectLoopRunning, 0);
                }
            });
        }

        private async Task ReconnectLoopAsync()
        {
            try { _reconnectCts.Cancel(); } catch { }
            try { _reconnectCts.Dispose(); } catch { }

            _reconnectCts = new CancellationTokenSource();
            CancellationToken ct = _reconnectCts.Token;

            int delay = ReconnectInitialSecs;

            while (AutoReconnect && !_disposed && !ct.IsCancellationRequested)
            {
                Log($"{delay}s 后重连...");

                try
                {
                    await Task.Delay(delay * 1000, ct).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                if (_ws?.ReadyState == WebSocketState.Open)
                    return;

                try
                {
                    Log("尝试重连...");

                    try { _ws?.Close(); } catch { }
                    await Task.Delay(300, ct).ConfigureAwait(false);

                    await ConnectAsync(ct).ConfigureAwait(false);

                    if (_ws?.ReadyState == WebSocketState.Open)
                        return;
                }
                catch (Exception ex)
                {
                    Log($"重连失败: {ex.Message}");
                    delay = Math.Min(delay * 2, ReconnectMaxSecs);
                }
            }
        }

        private void HandleMessage(string json)
        {
            try
            {
                var msg = SignalingMessage.FromJson(json);
                if (msg == null) return;

                AppLogger.Log("Signal", $"收到消息类型: {msg.Type}");

                switch (msg.Type)
                {
                    case MsgType.PeerJoined:
                        SetState(SignalingState.Paired);
                        OnPeerJoined?.Invoke();
                        break;

                    case MsgType.PeerLeft:
                        SetState(SignalingState.Connected);

                        if (msg.Graceful == true)
                        {
                            Log("对端主动断开");
                            OnPeerLeftGraceful?.Invoke();
                        }
                        else
                        {
                            Log("对端意外掉线");
                            OnPeerLeftAbnormal?.Invoke();
                        }
                        break;

                    case MsgType.Auth when msg.Password != null:
                        OnAuth?.Invoke(msg.Password);
                        break;

                    case MsgType.AuthResult when msg.AuthOk.HasValue:
                        OnAuthResult?.Invoke(msg.AuthOk.Value);
                        OnAuthResultInfo?.Invoke(msg.AuthOk.Value, msg.DeviceName);
                        break;

                    case MsgType.Bye:
                        SetState(SignalingState.Connected);
                        Log("收到对端 bye");
                        OnPeerLeftGraceful?.Invoke();
                        break;

                    case MsgType.Offer when msg.Sdp != null:
                        OnOffer?.Invoke(msg.Sdp);
                        break;

                    case MsgType.Answer when msg.Sdp != null:
                        OnAnswer?.Invoke(msg.Sdp);
                        break;

                    case MsgType.Ice when msg.Candidate != null:
                        OnIceCandidate?.Invoke(msg.Candidate);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"消息解析失败: {ex.Message}");
            }
        }

        private void TrySend(SignalingMessage msg)
        {
            if (_disposed) return;

            if (_ws?.ReadyState != WebSocketState.Open)
            {
                Log($"发送失败（未连接）: {msg.Type}");
                return;
            }

            _sendLock.Wait();

            try
            {
                if (_ws?.ReadyState != WebSocketState.Open)
                {
                    Log($"发送失败（未连接）: {msg.Type}");
                    return;
                }

                Log($"→ {msg.Type}");
                _ws.Send(msg.ToJson());
            }
            catch (Exception ex)
            {
                Log($"发送失败: {msg.Type}, {ex.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void SetState(SignalingState state)
        {
            if (State == state) return;

            State = state;
            Log($"状态变更: {state}");
            OnStateChanged?.Invoke(state);
        }

        private void Log(string msg)
        {
            OnLog?.Invoke($"[Signal][{Role}] {msg}");
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            AutoReconnect = false;
            _manualDisconnect = true;

            try { _reconnectCts.Cancel(); } catch { }
            try { _ws?.Close(); } catch { }

            try { _reconnectCts.Dispose(); } catch { }
            try { _sendLock.Dispose(); } catch { }
            try { _connectLock.Dispose(); } catch { }
        }
    }
}
