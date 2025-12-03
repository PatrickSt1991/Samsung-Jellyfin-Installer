using Fleck;
using System;

namespace Jellyfin2Samsung.Services;

public class TvLogService
{
    private WebSocketServer? _server;
    private IWebSocketConnection? _connection;

    public void StartLogServer(int port, Action<string> onMessage)
    {
        _server = new WebSocketServer($"ws://0.0.0.0:{port}");
        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                _connection = socket;
                onMessage("[Connected to TV]\n");
            };

            socket.OnClose = () =>
            {
                onMessage("[TV disconnected]\n");
            };

            socket.OnMessage = msg =>
            {
                onMessage(msg + "\n");
            };
        });
    }

    public void Stop()
    {
        _server?.Dispose();
        _server = null;
        _connection = null;
    }
}
