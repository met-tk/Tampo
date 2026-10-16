#if TAMPO_SYNC_SERVICE
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NihongoVocab.Services;

namespace NihongoVocab.SyncService
{
    /// <summary>
    /// 基于原生 Socket 实现的高性能 UDP 广播发现服务 (默认端口 52081)
    /// 支持物理以太网网线与无线 WiFi 双网卡自适应无缝穿透
    /// </summary>
    public sealed class UdpDiscoveryServer : IAsyncDisposable, IDisposable
    {
        public const int DefaultPort = 52081;
        public const int DefaultHttpPort = 52080;

        private readonly int _udpPort;
        private readonly int _httpPort;
        private Socket? _socket;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private bool _disposed;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public bool IsRunning => _socket != null && _listenTask != null && !_listenTask.IsCompleted;

        public UdpDiscoveryServer(int udpPort = DefaultPort, int httpPort = DefaultHttpPort)
        {
            _udpPort = udpPort;
            _httpPort = httpPort;
        }

        public void Start()
        {
            if (_socket != null) return;

            _cts = new CancellationTokenSource();

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.EnableBroadcast = true;

            // 关键修复：Windows 平台防御 WSAECONNRESET (10054) 导致的监听中断
            if (OperatingSystem.IsWindows())
            {
                const int SIO_UDP_CONNRESET = -1744830452; // 0x9800000C
                try
                {
                    socket.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                catch
                {
                }
            }

            socket.Bind(new IPEndPoint(IPAddress.Any, _udpPort));
            _socket = socket;

            _listenTask = Task.Run(ListenLoopAsync);
        }

        private async Task ListenLoopAsync()
        {
            var token = _cts?.Token ?? CancellationToken.None;
            byte[] buffer = new byte[4096];

            while (!token.IsCancellationRequested && _socket != null)
            {
                try
                {
                    EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                    var result = await _socket.ReceiveFromAsync(
                        buffer.AsMemory(0, buffer.Length),
                        SocketFlags.None,
                        remoteEp,
                        token);

                    int receivedBytes = result.ReceivedBytes;
                    var senderEndPoint = result.RemoteEndPoint as IPEndPoint;
                    if (receivedBytes <= 0 || senderEndPoint == null) continue;

                    string message = Encoding.UTF8.GetString(buffer, 0, receivedBytes);

                    // 匹配包含 "tampo_discover" 的探测数据报
                    if (message.Contains("tampo_discover", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleDiscoveryRequestAsync(senderEndPoint, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    if (token.IsCancellationRequested) break;
                    // 忽略 WinSock 10054 (ConnectionReset)
                    if (ex.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        continue;
                    }
                    CrashLogger.LogException(ex, "UdpDiscoveryServer.ListenLoopAsync");
                    await Task.Delay(100, token);
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    CrashLogger.LogException(ex, "UdpDiscoveryServer.ListenLoopAsync");
                    await Task.Delay(100, token);
                }
            }
        }

        private async Task HandleDiscoveryRequestAsync(IPEndPoint senderEndPoint, CancellationToken ct)
        {
            try
            {
                var responsePayload = new
                {
                    cmd = "tampo_beacon",
                    name = Environment.MachineName,
                    port = _httpPort,
                    version = HttpSyncServer.ServerVersion,
                    service = "TampoSyncService"
                };

                byte[] responseBytes = JsonSerializer.SerializeToUtf8Bytes(responsePayload, _jsonOptions);

                if (_socket != null)
                {
                    // 精确单播回传给发起探测的客户端 RemoteEndPoint
                    await _socket.SendToAsync(
                        responseBytes.AsMemory(0, responseBytes.Length),
                        SocketFlags.None,
                        senderEndPoint,
                        ct);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, $"UdpDiscoveryServer.HandleDiscoveryRequestAsync to {senderEndPoint}");
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _socket?.Close();
            }
            catch { }
            finally
            {
                _socket = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            if (_listenTask != null)
            {
                try { await _listenTask.ConfigureAwait(false); } catch { }
            }
            _cts?.Dispose();
        }
    }
}
#endif
