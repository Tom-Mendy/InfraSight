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

    public QrScanResult(string payload, Pose pose)
    {
        Payload = payload;
        Pose = pose;
    }
}
