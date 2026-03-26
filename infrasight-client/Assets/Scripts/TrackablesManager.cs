using Meta.XR.MRUtilityKit;
using UnityEngine;
using static OVRAnchor;

public class TrackablesManager : MonoBehaviour
{
    [SerializeField] private GameObject spawnPrefab;
    public void OnTrackableAdded(MRUKTrackable trackable)
    {

        if (trackable.TrackableType != TrackableType.QRCode) return;
        Debug.Log("I see a QRCode!");

        GameObject go = Instantiate(spawnPrefab, trackable.transform);

        QRTracker tracker = GetComponent<QRTracker>();
        tracker.qrID = trackable.MarkerPayloadString;

        go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    public void OnTrackableRemoved(MRUKTrackable trackable)
    {

    }
}
