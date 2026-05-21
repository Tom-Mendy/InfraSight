using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class InfraSightConnectionOrchestrator : IDisposable
{
    private const float FailedFeedbackLifetimeSeconds = 3f;

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
        ServerConnection connection = null;
        try
        {
            connection = await serverConnectionClient.ConnectToServerAsync(qrPayload);
        }
        finally
        {
            connectingEndpoints.Remove(endpoint);
        }

        if (connection == null)
        {
            visualizationManager.SetFeedbackState(feedbackObject, "CONNECTION_FAILED");
            visualizationManager.DestroyFeedback(feedbackObject, FailedFeedbackLifetimeSeconds);
            Debug.LogWarning($"Did not connect from QR payload endpoint {endpoint}.");
            return;
        }

        visualizationManager.DestroyFeedback(feedbackObject);
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
