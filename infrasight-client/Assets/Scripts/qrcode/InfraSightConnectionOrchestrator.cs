using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class InfraSightConnectionOrchestrator : IDisposable
{
    private readonly InfraSightMachineVisualizationManager visualizationManager;
    private readonly Dictionary<string, ServerDataPayload> pendingPayloads = new();
    private readonly HashSet<string> connectingEndpoints = new();
    private readonly object payloadLock = new();
    private readonly ServerConnectionClient serverConnectionClient;

    public InfraSightConnectionOrchestrator(InfraSightMachineVisualizationManager visualizationManager)
    {
        this.visualizationManager = visualizationManager;
        serverConnectionClient = new ServerConnectionClient();
        serverConnectionClient.PayloadReceived += OnServerPayloadReceived;
    }

    public async void ConnectQrPayload(string qrPayload, Pose pose)
    {
        if (!ServerConnectionClient.TryParseConnectionInfo(qrPayload, out QrConnectionInfo connectionInfo))
        {
            Debug.LogWarning("Scanned QR payload is not an InfraSight connection payload.");
            return;
        }

        string endpoint = ServerConnectionClient.BuildEndpoint(connectionInfo);
        if (visualizationManager.HasVisualization(endpoint) || connectingEndpoints.Contains(endpoint))
        {
            return;
        }

        connectingEndpoints.Add(endpoint);
        GameObject feedbackObject = visualizationManager.CreateFeedback(pose.position, pose.rotation);
        ServerConnection connection = await serverConnectionClient.ConnectToServerAsync(qrPayload);
        visualizationManager.DestroyFeedback(feedbackObject);
        connectingEndpoints.Remove(endpoint);

        if (connection == null)
        {
            Debug.LogWarning("Did not connect from this QR payload. It may not be a server connection QR code.");
            return;
        }

        visualizationManager.CreateMachineVisualization(connection, pose.position, pose.rotation);
    }

    public void ApplyPendingPayloads()
    {
        Dictionary<string, ServerDataPayload> payloadsToApply = null;
        lock (payloadLock)
        {
            if (pendingPayloads.Count > 0)
            {
                payloadsToApply = new Dictionary<string, ServerDataPayload>(pendingPayloads);
                pendingPayloads.Clear();
            }
        }

        if (payloadsToApply == null)
        {
            return;
        }

        foreach (KeyValuePair<string, ServerDataPayload> entry in payloadsToApply)
        {
            visualizationManager.ApplyPayload(entry.Key, entry.Value);
        }
    }

    public void Dispose()
    {
        if (serverConnectionClient != null)
        {
            serverConnectionClient.PayloadReceived -= OnServerPayloadReceived;
            serverConnectionClient.Dispose();
        }
    }

    private void OnServerPayloadReceived(ServerConnection connection, ServerDataPayload payload)
    {
        lock (payloadLock)
        {
            pendingPayloads[connection.Endpoint] = payload;
        }
    }
}
