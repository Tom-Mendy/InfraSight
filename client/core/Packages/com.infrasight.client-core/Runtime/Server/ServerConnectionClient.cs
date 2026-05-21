using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class ServerConnectionClient : IDisposable
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(8);

    public event Action<ServerConnection, ServerDataPayload> PayloadReceived;

    private readonly Dictionary<string, ServerConnection> connections = new();

    public Task<ServerConnection> ConnectToServerAsync(string qrPayload)
    {
        return ConnectToServerAsync(qrPayload, DefaultConnectTimeout);
    }

    public async Task<ServerConnection> ConnectToServerAsync(string qrPayload, TimeSpan connectTimeout)
    {
        if (!TryParseConnectionInfo(qrPayload, out QrConnectionInfo connectionInfo))
        {
            Debug.LogWarning($"Skipping QR payload because it does not contain connection data: '{qrPayload}'");
            return null;
        }

        string endpoint = BuildEndpoint(connectionInfo);
        if (connections.TryGetValue(endpoint, out ServerConnection existingConnection))
        {
            if (existingConnection.IsOpen)
            {
                return existingConnection;
            }

            existingConnection.PayloadReceived -= OnConnectionPayloadReceived;
            existingConnection.Dispose();
            connections.Remove(endpoint);
        }

        var connection = new ServerConnection(connectionInfo, endpoint);
        connection.PayloadReceived += OnConnectionPayloadReceived;
        connections[endpoint] = connection;

        bool connected = await connection.ConnectAsync(connectTimeout);
        if (connected)
        {
            return connection;
        }

        connection.PayloadReceived -= OnConnectionPayloadReceived;
        connection.Dispose();
        connections.Remove(endpoint);
        return null;
    }

    public static string BuildEndpoint(QrConnectionInfo connectionInfo)
    {
        string scheme = string.IsNullOrWhiteSpace(connectionInfo.scheme) ? "ws" : connectionInfo.scheme.Trim();
        string path = string.IsNullOrWhiteSpace(connectionInfo.path) ? "/ws" : connectionInfo.path.Trim();
        if (!path.StartsWith("/"))
        {
            path = "/" + path;
        }

        return $"{scheme}://{connectionInfo.ip}:{connectionInfo.port}{path}";
    }

    public static bool TryParseConnectionInfo(string qrPayload, out QrConnectionInfo connectionInfo)
    {
        connectionInfo = null;
        if (string.IsNullOrWhiteSpace(qrPayload))
        {
            return false;
        }

        string trimmed = qrPayload.Trim();

        if (trimmed.StartsWith("{"))
        {
            return TryParseJsonConnectionInfo(trimmed, out connectionInfo);
        }

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
                port = uri.Port,
                scheme = uri.Scheme,
                path = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/ws" : uri.AbsolutePath
            };

            return true;
        }

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

    private static bool TryParseJsonConnectionInfo(string json, out QrConnectionInfo connectionInfo)
    {
        connectionInfo = null;
        try
        {
            QrConnectionInfo parsed = JsonConvert.DeserializeObject<QrConnectionInfo>(json);
            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.ip) && parsed.port > 0)
            {
                connectionInfo = parsed;
                return true;
            }

            JObject parsedObject = JObject.Parse(json);
            string ws = parsedObject.Value<string>("ws");
            if (!string.IsNullOrWhiteSpace(ws)
                && Uri.TryCreate(ws, UriKind.Absolute, out Uri uri)
                && !string.IsNullOrWhiteSpace(uri.Host)
                && uri.Port > 0)
            {
                connectionInfo = new QrConnectionInfo
                {
                    name = parsedObject.Value<string>("name") ?? uri.Host,
                    ip = uri.Host,
                    port = uri.Port,
                    scheme = uri.Scheme,
                    path = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/ws" : uri.AbsolutePath
                };
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void OnConnectionPayloadReceived(ServerConnection connection, ServerDataPayload payload)
    {
        PayloadReceived?.Invoke(connection, payload);
    }

    public void Dispose()
    {
        foreach (ServerConnection connection in connections.Values)
        {
            connection.PayloadReceived -= OnConnectionPayloadReceived;
            connection.Dispose();
        }

        connections.Clear();
    }
}

public class ServerConnection : IDisposable
{
    public event Action<ServerConnection, ServerDataPayload> PayloadReceived;

    public string Endpoint { get; }
    public string MachineName { get; private set; }
    public bool IsOpen => webSocket != null && webSocket.State == WebSocketState.Open;

    private ClientWebSocket webSocket;
    private CancellationTokenSource listenCancellationTokenSource;
    private Task listenTask;

    public ServerConnection(QrConnectionInfo connectionInfo, string endpoint)
    {
        Endpoint = endpoint;
        MachineName = string.IsNullOrWhiteSpace(connectionInfo.name) ? connectionInfo.ip : connectionInfo.name;
    }

    public async Task<bool> ConnectAsync(TimeSpan timeout)
    {
        webSocket = new ClientWebSocket();
        listenCancellationTokenSource = new CancellationTokenSource();

        try
        {
            using var connectCancellationTokenSource = new CancellationTokenSource(timeout);
            await webSocket.ConnectAsync(new Uri(Endpoint), connectCancellationTokenSource.Token);
            Debug.Log($"Connected to websocket server at {Endpoint}");
            listenTask = ListenForMessagesAsync(webSocket, listenCancellationTokenSource.Token);
            return true;
        }
        catch (OperationCanceledException exception)
        {
            Debug.LogError($"Timed out connecting to websocket server at {Endpoint} after {timeout.TotalSeconds:0.#}s: {exception.Message}");
            webSocket.Dispose();
            webSocket = null;
            listenCancellationTokenSource.Dispose();
            listenCancellationTokenSource = null;
            return false;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to connect to websocket server at {Endpoint}: {exception.Message}");
            webSocket.Dispose();
            webSocket = null;
            listenCancellationTokenSource.Dispose();
            listenCancellationTokenSource = null;
            return false;
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
                HandleMessage(message);
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

    private void HandleMessage(string message)
    {
        try
        {
            JObject envelope = JObject.Parse(message);
            string messageType = envelope.Value<string>("type");
            if (string.Equals(messageType, "connection", StringComparison.OrdinalIgnoreCase))
            {
                string machineName = envelope.Value<string>("machine_name");
                if (!string.IsNullOrWhiteSpace(machineName))
                {
                    MachineName = machineName;
                }

                return;
            }

            ServerDataPayload payload = envelope.ToObject<ServerDataPayload>();
            if (payload?.machine == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(payload.machine.name))
            {
                payload.machine.name = MachineName;
            }

            PayloadReceived?.Invoke(this, payload);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Failed to process websocket message from {Endpoint}: {exception.Message}");
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
