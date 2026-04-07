using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public class ServerConnectionClient : IDisposable
{
    public event Action<string> MessageReceived;

    private ClientWebSocket webSocket;
    private string connectedEndpoint;
    private CancellationTokenSource listenCancellationTokenSource;
    private Task listenTask;

    public async Task<bool> ConnectToServerAsync(string qrPayload)
    {
        if (!TryParseConnectionInfo(qrPayload, out QrConnectionInfo connectionInfo))
        {
            Debug.LogWarning($"Skipping QR payload because it does not contain connection data: '{qrPayload}'");
            return false;
        }

        string endpoint = $"ws://{connectionInfo.ip}:{connectionInfo.port}/ws";
        if (webSocket != null)
        // if (webSocket != null && webSocket.State == WebSocketState.Open && connectedEndpoint == endpoint)
        {
            return webSocket.State == WebSocketState.Open;
        }

        await DisconnectAsync("Switching QR target");

        webSocket = new ClientWebSocket();
        listenCancellationTokenSource = new CancellationTokenSource();

        try
        {
            await webSocket.ConnectAsync(new Uri(endpoint), CancellationToken.None);
            connectedEndpoint = endpoint;
            Debug.Log($"Connected to websocket server at {endpoint}");
            listenTask = ListenForMessagesAsync(webSocket, listenCancellationTokenSource.Token);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to connect to websocket server at {endpoint}: {exception.Message}");
            webSocket.Dispose();
            webSocket = null;
            connectedEndpoint = null;
            listenCancellationTokenSource.Dispose();
            listenCancellationTokenSource = null;
            return false;
        }
    }

    private static bool TryParseConnectionInfo(string qrPayload, out QrConnectionInfo connectionInfo)
    {
        connectionInfo = null;
        if (string.IsNullOrWhiteSpace(qrPayload))
        {
            return false;
        }

        string trimmed = qrPayload.Trim();

        // Preferred payload format from docs: {"name":"...","ip":"...","port":8080}
        if (trimmed.StartsWith("{"))
        {
            try
            {
                QrConnectionInfo parsed = JsonConvert.DeserializeObject<QrConnectionInfo>(trimmed);
                if (parsed != null && !string.IsNullOrWhiteSpace(parsed.ip) && parsed.port > 0)
                {
                    connectionInfo = parsed;
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        // Also accept direct websocket URL payloads like ws://192.168.1.10:8080/ws
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri)
            && (string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(uri.Host)
            && uri.Port > 0)
        {
            connectionInfo = new QrConnectionInfo
            {
                name = uri.Host,
                ip = uri.Host,
                port = uri.Port
            };

            return true;
        }

        // Also accept compact host:port payloads like 192.168.1.10:8080
        string[] parts = trimmed.Split(':');
        if (parts.Length == 2
            && !string.IsNullOrWhiteSpace(parts[0])
            && int.TryParse(parts[1], out int parsedPort)
            && parsedPort > 0)
        {
            connectionInfo = new QrConnectionInfo
            {
                name = parts[0],
                ip = parts[0],
                port = parsedPort
            };

            return true;
        }

        return false;
    }

    private async Task ListenForMessagesAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("WebSocket server closed the connection.");
                        connectedEndpoint = null;
                        return;
                    }

                    messageStream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text || messageStream.Length == 0)
                {
                    continue;
                }

                string message = Encoding.UTF8.GetString(messageStream.ToArray());
                Debug.Log($"WebSocket message received: {message}");
                MessageReceived?.Invoke(message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.LogError($"WebSocket listen failed: {exception.Message}");
        }
    }

    private async Task DisconnectAsync(string reason)
    {
        listenCancellationTokenSource?.Cancel();

        if (webSocket != null)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to close previous websocket cleanly: {exception.Message}");
            }

            webSocket.Dispose();
            webSocket = null;
        }

        connectedEndpoint = null;

        if (listenTask != null)
        {
            try
            {
                await listenTask;
            }
            catch (Exception)
            {
            }

            listenTask = null;
        }

        listenCancellationTokenSource?.Dispose();
        listenCancellationTokenSource = null;
    }

    public void Dispose()
    {
        listenCancellationTokenSource?.Cancel();
        webSocket?.Dispose();
        listenCancellationTokenSource?.Dispose();
    }
}
