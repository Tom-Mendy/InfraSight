using System;
using UnityEngine;

public interface IQrScanProvider
{
    event Action<QrScanResult> QrDetected;
    bool IsSupported { get; }
    void StartScanning();
    void StopScanning();
}

public readonly struct QrScanResult
{
    public string Payload { get; }
    public Pose Pose { get; }
    public bool HasTrackedPose { get; }

    public QrScanResult(string payload, Pose pose)
        : this(payload, pose, true)
    {
    }

    public QrScanResult(string payload, Pose pose, bool hasTrackedPose)
    {
        Payload = payload;
        Pose = pose;
        HasTrackedPose = hasTrackedPose;
    }
}
