using System;
using UnityEngine;

#if INFRASIGHT_HAS_ARFOUNDATION
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#endif

public class AndroidArCoreTrackableProvider : MonoBehaviour, ITrackableProvider
{
    public event Action<TrackableData> TrackableAdded;
    public event Action<TrackableData> TrackableRemoved;

#if INFRASIGHT_HAS_ARFOUNDATION
    [SerializeField] private ARTrackedImageManager trackedImageManager;
    [SerializeField] private bool includeLimitedTracking;
#endif

    public bool IsSupported =>
#if INFRASIGHT_HAS_ARFOUNDATION
        Application.platform == RuntimePlatform.Android && trackedImageManager != null;
#else
        false;
#endif

    public void StartProvider()
    {
#if INFRASIGHT_HAS_ARFOUNDATION
        if (trackedImageManager == null)
        {
            Debug.LogWarning("AndroidArCoreTrackableProvider requires an ARTrackedImageManager reference.");
            return;
        }

        trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
#endif
    }

    public void StopProvider()
    {
#if INFRASIGHT_HAS_ARFOUNDATION
        if (trackedImageManager != null)
        {
            trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
        }
#endif
    }

#if INFRASIGHT_HAS_ARFOUNDATION
    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
    {
        foreach (ARTrackedImage tracked in args.added)
        {
            EmitAddedIfTracked(tracked);
        }

        foreach (ARTrackedImage tracked in args.updated)
        {
            EmitAddedIfTracked(tracked);
        }

        foreach (ARTrackedImage tracked in args.removed)
        {
            TrackableRemoved?.Invoke(ToTrackableData(tracked));
        }
    }

    private void EmitAddedIfTracked(ARTrackedImage tracked)
    {
        if (tracked == null)
        {
            return;
        }

        TrackingState state = tracked.trackingState;
        bool isTracked = state == TrackingState.Tracking || (includeLimitedTracking && state == TrackingState.Limited);
        if (!isTracked)
        {
            return;
        }

        TrackableAdded?.Invoke(ToTrackableData(tracked));
    }

    private static TrackableData ToTrackableData(ARTrackedImage tracked)
    {
        // We map tracked image reference names to QR IDs used by existing gameplay logic.
        string payloadId = tracked.referenceImage.name;

        return TrackableData.CreateQr(
            tracked.trackableId.ToString(),
            payloadId,
            new Pose(tracked.transform.position, tracked.transform.rotation),
            tracked.transform
        );
    }
#endif
}