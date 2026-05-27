#if INFRASIGHT_META_QUEST || UNITY_META_QUEST
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class MetaQuestQrScanProvider : QrScanProviderBehaviour
{
    public override bool IsSupported => true;

    public void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable == null || trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
        {
            return;
        }

        RaiseQrDetected(
            trackable.MarkerPayloadString,
            new Pose(trackable.transform.position, trackable.transform.rotation));
    }

    public void OnTrackableRemoved(MRUKTrackable trackable)
    {
        // Connections and machine views persist after QR tracking is lost.
    }
}
#endif
