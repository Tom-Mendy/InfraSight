using System;
using UnityEngine;

public enum TrackableKind
{
    Unknown = 0,
    QRCode = 1,
}

[Serializable]
public struct TrackableData
{
    public string TrackableId;
    public string PayloadId;
    public TrackableKind Kind;
    public Pose Pose;
    public Transform ParentTransform;

    public static TrackableData CreateQr(string trackableId, string payloadId, Pose pose, Transform parentTransform)
    {
        return new TrackableData
        {
            TrackableId = trackableId,
            PayloadId = payloadId,
            Kind = TrackableKind.QRCode,
            Pose = pose,
            ParentTransform = parentTransform,
        };
    }
}

public interface ITrackableProvider
{
    event Action<TrackableData> TrackableAdded;
    event Action<TrackableData> TrackableRemoved;

    bool IsSupported { get; }
    void StartProvider();
    void StopProvider();
}