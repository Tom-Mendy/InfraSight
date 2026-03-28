using System;
using UnityEngine;

#if INFRASIGHT_HAS_META_MRUK
using Meta.XR.MRUtilityKit;
using static OVRAnchor;
#endif

public class QuestTrackableProvider : MonoBehaviour, ITrackableProvider
{
    public event Action<TrackableData> TrackableAdded;
    public event Action<TrackableData> TrackableRemoved;

    public bool IsSupported =>
#if INFRASIGHT_HAS_META_MRUK
        Application.platform == RuntimePlatform.Android && IsLikelyQuestDevice();
#else
        false;
#endif

    public void StartProvider()
    {
    }

    public void StopProvider()
    {
    }

    private static bool IsLikelyQuestDevice()
    {
        string model = (SystemInfo.deviceModel ?? string.Empty).ToLowerInvariant();
        string deviceName = (SystemInfo.deviceName ?? string.Empty).ToLowerInvariant();

        return model.Contains("quest") || deviceName.Contains("quest") || model.Contains("hollywood");
    }

#if INFRASIGHT_HAS_META_MRUK
    // Hook these methods to MRUK trackable events in the scene.
    public void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (TryConvertTrackable(trackable, out TrackableData data))
        {
            TrackableAdded?.Invoke(data);
        }
    }

    public void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (TryConvertTrackable(trackable, out TrackableData data))
        {
            TrackableRemoved?.Invoke(data);
        }
    }

    private static bool TryConvertTrackable(MRUKTrackable trackable, out TrackableData data)
    {
        data = default;

        if (trackable == null || trackable.TrackableType != TrackableType.QRCode)
        {
            return false;
        }

        data = TrackableData.CreateQr(
            trackable.GetInstanceID().ToString(),
            trackable.MarkerPayloadString,
            new Pose(trackable.transform.position, trackable.transform.rotation),
            trackable.transform
        );

        return true;
    }
#endif
}