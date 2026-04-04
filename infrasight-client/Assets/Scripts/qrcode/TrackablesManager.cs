using Meta.XR.MRUtilityKit;
using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;

public class TrackablesManager : MonoBehaviour
{
    [SerializeField] private GameObject spawnSpherePrefab;
    [SerializeField] private GameObject spawnCubePrefab;
    [SerializeField] private GameObject machineVisualizationPrefab;

    [SerializeField] private GameObject feedbackPrefab;
    private GameObject feedbackInstance;
    private GameObject machineVisualization;
    private readonly Dictionary<string, GameObject> containerVisualizations = new Dictionary<string, GameObject>();

    private readonly object payloadLock = new object();
    private ServerDataPayload pendingPayload;

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
        try
        {
            ServerDataPayload payload = JsonConvert.DeserializeObject<ServerDataPayload>(message);
            if (payload == null)
            {
                return;
            }

            lock (payloadLock)
            {
                pendingPayload = payload;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Failed to deserialize server payload: {ex.Message}");
        }
    }

    public async void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;
        string qrID = trackable.MarkerPayloadString;
        qrID = "{\"name\":\"Tom_Zeph-G-14\",\"ip\":\"192.168.245.1\",\"port\":8080}"; // test mode with hardcoded QR payload

        Debug.Log($"I see a {qrID}!");

        // Instantiate feedback prefab while connecting
        if (feedbackPrefab != null && feedbackInstance == null)
        {
            feedbackInstance = Instantiate(feedbackPrefab, trackable.transform);
            feedbackInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            QRTracker feedbackTracker = feedbackInstance.GetComponent<QRTracker>();
            if (feedbackTracker != null)
            {
                feedbackTracker.qrID = "CONNECTING";
            }
        }

        bool connected = await serverConnectionClient.ConnectToServerAsync(qrID);

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
                _ => machineVisualizationPrefab
            };

            machineVisualization = Instantiate(prefabToSpawn, trackable.transform);
            machineVisualization.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            QRTracker tracker = machineVisualization.GetComponent<QRTracker>();
            if (tracker != null)
            {
                tracker.qrID = qrID;
            }
        }
        else
        {
            Debug.LogWarning("Did not connect from this QR payload. It may not be a server connection QR code.");
        }
    }

    public void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (feedbackInstance != null)
        {
            Destroy(feedbackInstance);
            feedbackInstance = null;
        }

        if (machineVisualization != null)
        {
            Destroy(machineVisualization);
            machineVisualization = null;
        }

        foreach (GameObject container in containerVisualizations.Values)
        {
            if (container != null)
            {
                Destroy(container);
            }
        }

        containerVisualizations.Clear();
    }

    private void Update()
    {
        ServerDataPayload payloadToApply = null;
        lock (payloadLock)
        {
            if (pendingPayload != null)
            {
                payloadToApply = pendingPayload;
                pendingPayload = null;
            }
        }

        if (payloadToApply != null)
        {
            ApplyPayload(payloadToApply);
        }
    }

    private void ApplyPayload(ServerDataPayload payload)
    {
        if (machineVisualization != null && payload.machine != null)
        {
            float normalizedCpu = Mathf.Clamp01(payload.machine.cpu / 100f);
            float normalizedRam = Mathf.Clamp01(payload.machine.ram / 100f);

            MachineVisualization machineVis = machineVisualization.GetComponent<MachineVisualization>();
            if (machineVis != null)
            {
                machineVis.UpdateVisualization(payload.machine);
            }
            else
            {
                Debug.LogWarning("Machine visualization prefab is missing the MachineVisualization component.");
                // CPU controls size between 0.4 and 1.4 units.
                float scale = Mathf.Lerp(0.4f, 1.4f, normalizedCpu);
                machineVisualization.transform.localScale = new Vector3(scale, scale, scale);

                Renderer renderer = machineVisualization.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    // RAM controls color from green (low) to red (high).
                    renderer.material.color = Color.Lerp(Color.green, Color.red, normalizedRam);
                }
            }
        }

        HashSet<string> activeContainerIds = new HashSet<string>();
        if (payload.container != null)
        {
            for (int i = 0; i < payload.container.Length; i++)
            {
                ContainerDataPayload containerData = payload.container[i];
                if (containerData == null || string.IsNullOrWhiteSpace(containerData.id))
                {
                    continue;
                }

                activeContainerIds.Add(containerData.id);
                if (!containerVisualizations.TryGetValue(containerData.id, out GameObject containerGo) || containerGo == null)
                {
                    if (spawnCubePrefab == null || machineVisualization == null)
                    {
                        continue;
                    }

                    containerGo = Instantiate(spawnCubePrefab, machineVisualization.transform.parent);
                    containerVisualizations[containerData.id] = containerGo;
                }

                float angle = (360f / Mathf.Max(1, payload.container.Length)) * i;
                float radius = 0.8f;
                Vector3 offset = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0.2f, Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
                containerGo.transform.localPosition = machineVisualization.transform.localPosition + offset;
                containerGo.transform.localRotation = Quaternion.identity;
                containerGo.transform.localScale = Vector3.one * 0.2f;

                Renderer containerRenderer = containerGo.GetComponentInChildren<Renderer>();
                if (containerRenderer != null)
                {
                    containerRenderer.material.color = containerData.status == "running" ? Color.green : Color.red;
                }
            }
        }

        List<string> staleIds = new List<string>();
        foreach (string containerId in containerVisualizations.Keys)
        {
            if (!activeContainerIds.Contains(containerId))
            {
                staleIds.Add(containerId);
            }
        }

        foreach (string staleId in staleIds)
        {
            GameObject staleObject = containerVisualizations[staleId];
            if (staleObject != null)
            {
                Destroy(staleObject);
            }

            containerVisualizations.Remove(staleId);
        }
    }

}
