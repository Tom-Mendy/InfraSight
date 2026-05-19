using System;
using UnityEngine;

public abstract class QrScanProviderBehaviour : MonoBehaviour, IQrScanProvider
{
    public event Action<QrScanResult> QrDetected;
    public abstract bool IsSupported { get; }

    public virtual void StartScanning()
    {
        enabled = true;
    }

    public virtual void StopScanning()
    {
        enabled = false;
    }

    protected void RaiseQrDetected(string payload, Pose pose)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        QrDetected?.Invoke(new QrScanResult(payload, pose));
    }
}
