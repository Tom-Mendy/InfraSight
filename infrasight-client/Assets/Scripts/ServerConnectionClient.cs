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

    public async Task ConnectToServerAsync(string qrPayload)
    {
        QrConnectionInfo connectionInfo;
        try
        {
            connectionInfo = JsonConvert.DeserializeObject<QrConnectionInfo>(qrPayload);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to parse QR payload '{qrPayload}': {exception.Message}");
            return;
        }

        if (connectionInfo == null || string.IsNullOrWhiteSpace(connectionInfo.ip) || connectionInfo.port <= 0)
        {
            Debug.LogError($"QR payload is missing websocket connection details: {qrPayload}");
            return;
        }

        string endpoint = $"ws://{connectionInfo.ip}:{connectionInfo.port}/ws";
        if (webSocket != null)
        // if (webSocket != null && webSocket.State == WebSocketState.Open && connectedEndpoint == endpoint)
        {
            return;
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
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to connect to websocket server at {endpoint}: {exception.Message}");
            webSocket.Dispose();
            webSocket = null;
            connectedEndpoint = null;
            listenCancellationTokenSource.Dispose();
            listenCancellationTokenSource = null;
        }
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
