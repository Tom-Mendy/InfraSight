using Meta.XR.MRUtilityKit;
using UnityEngine;
using static OVRAnchor;

public class TrackablesManager : MonoBehaviour
{
    [SerializeField] private GameObject spawnSpherePrefab;
    [SerializeField] private GameObject spawnCubePrefab;
    public void OnTrackableAdded(MRUKTrackable trackable)
    {

        if (trackable.TrackableType != TrackableType.QRCode) return;
        string qrID = trackable.MarkerPayloadString;
        Debug.Log("I see a QRCode!");

        GameObject prefabToSpawn = null;

        if (qrID == "QR_Sphere")
        {
            prefabToSpawn = spawnSpherePrefab;
        }
        if (qrID == "QR_Cube")
        {
            prefabToSpawn = spawnCubePrefab;
        }
        GameObject go = Instantiate(prefabToSpawn, trackable.transform);
        go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        QRTracker tracker = GetComponent<QRTracker>();
        tracker.qrID = qrID;

    }

    public void OnTrackableRemoved(MRUKTrackable trackable)
    {

    }
}
