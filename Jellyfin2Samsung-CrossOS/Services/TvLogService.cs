using Fleck;
using Jellyfin2Samsung.Models;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Services
{
    public class TvLogService
    {
        private WebSocketServer? _server;
        private IWebSocketConnection? _connection;
        private CancellationTokenSource? _cts;

        public void StartLogServer(
            int port,
            Action<string> onMessage,
            Action<TvLogConnectionStatus> onStatusChanged)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                FleckLog.Level = LogLevel.Warn;
                FleckLog.LogAction = (level, msg, ex) =>
                    onMessage($"[Fleck {level}] {msg} {ex}\n");

                _server = new WebSocketServer($"ws://0.0.0.0:{port}");
                _server.Start(socket =>
                {
                    socket.OnOpen = () =>
                    {
                        _connection = socket;
                        onStatusChanged(TvLogConnectionStatus.Connected);
                        onMessage("[Connected to TV]\n");
                    };

                    socket.OnClose = () =>
                    {
                        _connection = null;
                        onStatusChanged(TvLogConnectionStatus.Listening);
                        onMessage("[TV disconnected]\n");
                    };

                    socket.OnMessage = msg =>
                    {
                        onMessage(msg + "\n");
                    };
                });

                onStatusChanged(TvLogConnectionStatus.Listening);
                onMessage($"[Listening on ws://0.0.0.0:{port}]\n");

                _ = MonitorForConnectionsAsync(
                    port,
                    onMessage,
                    onStatusChanged,
                    _cts.Token);
            }
            catch (Exception ex)
            {
                onStatusChanged(TvLogConnectionStatus.Stopped);
                onMessage($"[Failed to start server: {ex.Message}]\n");
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;

            _server?.Dispose();
            _server = null;
            _connection = null;
        }

        private async Task MonitorForConnectionsAsync(
            int port,
            Action<string> onMessage,
            Action<TvLogConnectionStatus> onStatusChanged,
            CancellationToken token)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);

                if (_connection != null || token.IsCancellationRequested)
                    return;

                onStatusChanged(TvLogConnectionStatus.NoConnections);

                onMessage(
                    "[No incoming connections detected]\n" +
                    "If the TV cannot connect, your firewall may be blocking this port.\n\n" +
                    GetFirewallHelpText(port));
            }
            catch (TaskCanceledException) { }
        }

        private static string GetFirewallHelpText(int port)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return
                    "Windows:\n" +
                    $"  netstat -ano | findstr {port}\n\n" +
                    $"  New-NetFirewallRule -DisplayName \"Jellyfin2Samsung Logs\" \\\n" +
                    $"    -Direction Inbound -Protocol TCP -LocalPort {port} -Action Allow\n";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return
                    $"Linux:\n  sudo ufw allow {port}/tcp\n  sudo ufw reload\n";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return
                    "macOS:\n" +
                    "  System Settings → Network → Firewall → Options\n" +
                    "  Allow incoming connections for Jellyfin2Samsung\n";
            }

            return "Ensure your firewall allows inbound TCP connections.\n";
        }
    }
}
