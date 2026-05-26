using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class InfraSightConnectionOrchestrator : IDisposable
{
    private const float FailedFeedbackLifetimeSeconds = 3f;

    private readonly InfraSightMachineVisualizationManager visualizationManager;
    private readonly Dictionary<string, ServerDataPayload> pendingPayloads = new();
    private readonly Dictionary<string, ConnectionAttempt> connectionAttempts = new();
    private readonly object payloadLock = new();
    private readonly ServerConnectionClient serverConnectionClient;

    public InfraSightConnectionOrchestrator(InfraSightMachineVisualizationManager visualizationManager)
    {
        this.visualizationManager = visualizationManager;
        serverConnectionClient = new ServerConnectionClient();
        serverConnectionClient.PayloadReceived += OnServerPayloadReceived;
    }

    public async void ConnectQrPayload(string qrPayload, Pose pose, bool hasTrackedPose = true)
    {
        if (!ServerConnectionClient.TryParseConnectionInfo(qrPayload, out QrConnectionInfo connectionInfo))
        {
            Debug.LogWarning("Scanned QR payload is not an InfraSight connection payload.");
            return;
        }

        string endpoint = ServerConnectionClient.BuildEndpoint(connectionInfo);
        if (connectionAttempts.TryGetValue(endpoint, out ConnectionAttempt currentAttempt))
        {
            UpdateAttemptPose(endpoint, currentAttempt, pose, hasTrackedPose);
            return;
        }

        if (visualizationManager.HasVisualization(endpoint))
        {
            return;
        }

        string machineName = string.IsNullOrWhiteSpace(connectionInfo.name) ? connectionInfo.ip : connectionInfo.name;
        var attempt = new ConnectionAttempt(machineName, pose, hasTrackedPose)
        {
            FeedbackObject = visualizationManager.CreateFeedback(pose.position, pose.rotation, machineName)
        };
        connectionAttempts[endpoint] = attempt;

        ServerConnection connection = await serverConnectionClient.ConnectToServerAsync(qrPayload);
        if (!connectionAttempts.TryGetValue(endpoint, out ConnectionAttempt activeAttempt)
            || activeAttempt != attempt)
        {
            return;
        }

        if (connection == null)
        {
            attempt.HasFailed = true;
            attempt.FailedAt = Time.unscaledTime;
            visualizationManager.SetFeedbackFailed(attempt.FeedbackObject, attempt.MachineName);
            visualizationManager.DestroyFeedback(attempt.FeedbackObject, FailedFeedbackLifetimeSeconds);
            Debug.LogWarning($"Did not connect from QR payload endpoint {endpoint}.");
            return;
        }

        attempt.Connection = connection;
        TryCreateMachineVisualization(endpoint, attempt);
    }

    public void ApplyPendingPayloads()
    {
        RemoveExpiredFailedAttempts();

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
        foreach (ConnectionAttempt attempt in connectionAttempts.Values)
        {
            visualizationManager.DestroyFeedback(attempt.FeedbackObject);
        }

        connectionAttempts.Clear();
        serverConnectionClient.PayloadReceived -= OnServerPayloadReceived;
        serverConnectionClient.Dispose();
    }

    private void UpdateAttemptPose(string endpoint, ConnectionAttempt attempt, Pose pose, bool hasTrackedPose)
    {
        if (!hasTrackedPose || attempt.HasTrackedPose)
        {
            return;
        }

        attempt.Pose = pose;
        attempt.HasTrackedPose = true;
        if (attempt.VisualizationCreated)
        {
            visualizationManager.UpdateMachineVisualizationPose(endpoint, pose.position, pose.rotation);
            Debug.Log($"Reanchored machine visualization at tracked QR pose for endpoint {endpoint}.");
            connectionAttempts.Remove(endpoint);
            return;
        }

        visualizationManager.UpdateFeedbackPose(attempt.FeedbackObject, pose.position, pose.rotation);
        TryCreateMachineVisualization(endpoint, attempt);
    }

    private void TryCreateMachineVisualization(string endpoint, ConnectionAttempt attempt)
    {
        if (attempt.Connection == null || attempt.HasFailed || attempt.VisualizationCreated)
        {
            return;
        }

        visualizationManager.DestroyFeedback(attempt.FeedbackObject);
        visualizationManager.CreateMachineVisualization(attempt.Connection, attempt.Pose.position, attempt.Pose.rotation);
        attempt.VisualizationCreated = true;
        Debug.Log(
            attempt.HasTrackedPose
                ? $"Created machine visualization at tracked QR pose for endpoint {endpoint}."
                : $"Created machine visualization at provisional QR pose for endpoint {endpoint}; awaiting tracked pose.");

        if (attempt.HasTrackedPose)
        {
            connectionAttempts.Remove(endpoint);
        }
    }

    private void RemoveExpiredFailedAttempts()
    {
        List<string> expiredEndpoints = null;
        foreach (KeyValuePair<string, ConnectionAttempt> entry in connectionAttempts)
        {
            if (!entry.Value.HasFailed
                || Time.unscaledTime - entry.Value.FailedAt < FailedFeedbackLifetimeSeconds)
            {
                continue;
            }

            expiredEndpoints ??= new List<string>();
            expiredEndpoints.Add(entry.Key);
        }

        if (expiredEndpoints == null)
        {
            return;
        }

        foreach (string endpoint in expiredEndpoints)
        {
            connectionAttempts.Remove(endpoint);
        }
    }

    private void OnServerPayloadReceived(ServerConnection connection, ServerDataPayload payload)
    {
        lock (payloadLock)
        {
            pendingPayloads[connection.Endpoint] = payload;
        }
    }

    private sealed class ConnectionAttempt
    {
        public string MachineName { get; }
        public GameObject FeedbackObject { get; set; }
        public ServerConnection Connection { get; set; }
        public Pose Pose { get; set; }
        public bool HasTrackedPose { get; set; }
        public bool VisualizationCreated { get; set; }
        public bool HasFailed { get; set; }
        public float FailedAt { get; set; }

        public ConnectionAttempt(string machineName, Pose pose, bool hasTrackedPose)
        {
            MachineName = machineName;
            Pose = pose;
            HasTrackedPose = hasTrackedPose;
        }
    }
}
