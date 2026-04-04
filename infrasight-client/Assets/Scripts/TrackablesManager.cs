using Meta.XR.MRUtilityKit;
using UnityEngine;
using static OVRAnchor;

public class TrackablesManager : MonoBehaviour
{
    [SerializeField] private GameObject spawnSpherePrefab;
    [SerializeField] private GameObject spawnCubePrefab;

    private ServerConnectionClient serverConnectionClient;

    private void Awake()
    {
        serverConnectionClient = new ServerConnectionClient();
    }

    public async void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != TrackableType.QRCode) return;
        // string qrID = trackable.MarkerPayloadString;
        string qrID = "{\"name\":\"Tom_Zeph-G-14\",\"ip\":\"192.168.245.1\",\"port\":8080}";
        Debug.Log($"I see a {qrID}!");
        await serverConnectionClient.ConnectToServerAsync(qrID);

        GameObject prefabToSpawn = qrID switch
        {
            "QR_Sphere" => spawnSpherePrefab,
            "QR_Cube" => spawnCubePrefab,
            _ => spawnSpherePrefab
        };

        GameObject go = Instantiate(prefabToSpawn, trackable.transform);
        go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        QRTracker tracker = go.GetComponent<QRTracker>();
        tracker.qrID = qrID;
    }

    public void OnTrackableRemoved(MRUKTrackable trackable)
    {

    }

    private void OnDestroy()
    {
        serverConnectionClient?.Dispose();
    }

}
