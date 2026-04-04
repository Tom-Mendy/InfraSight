using Meta.XR.MRUtilityKit;
using UnityEngine;
using static OVRAnchor;

public class TrackablesManager : MonoBehaviour
{
    [SerializeField] private GameObject spawnSpherePrefab;
    [SerializeField] private GameObject spawnCubePrefab;

    [SerializeField] private GameObject feedbackPrefab;
    private GameObject feedbackInstance;

    private ServerConnectionClient serverConnectionClient;

    private void Awake()
    {
        serverConnectionClient = new ServerConnectionClient();
        serverConnectionClient.MessageReceived += OnServerMessageReceived;
    }

    private void OnDestroy()
    {
        if (serverConnectionClient != null)
        {
            serverConnectionClient.MessageReceived -= OnServerMessageReceived;
            serverConnectionClient.Dispose();
        }
    }


    private void OnServerMessageReceived(string message)
    {
        // Parse server data and update metrics visualization
        Debug.Log($"Received server data: {message}");
        // TODO: Deserialize ServerDataPayload and update visualization (sphere size/color, containers, etc.)
    }

    public async void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != TrackableType.QRCode) return;
        // string qrID = trackable.MarkerPayloadString;
        string qrID = "{\"name\":\"Tom_Zeph-G-14\",\"ip\":\"192.168.245.1\",\"port\":8080}";
        Debug.Log($"I see a {qrID}!");

        // Instantiate feedback prefab while connecting
        if (feedbackPrefab != null && feedbackInstance == null)
        {
            feedbackInstance = Instantiate(feedbackPrefab, trackable.transform);
            feedbackInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        bool connected = false;
        try
        {
            await serverConnectionClient.ConnectToServerAsync(qrID);
            // If no exception, assume connected
            connected = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Connection failed: {ex.Message}");
        }

        // Destroy feedback prefab after connection attempt
        if (feedbackInstance != null)
        {
            Destroy(feedbackInstance);
            feedbackInstance = null;
        }

        if (connected)
        {
            // Spawn metrics visualization prefab (sphere/cube) as before
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
        else
        {
            // Optionally, show error UI or feedback here
            Debug.LogError("Failed to connect to server. Show error feedback to user.");
        }
    }

    public void OnTrackableRemoved(MRUKTrackable trackable)
    {

    }

}
