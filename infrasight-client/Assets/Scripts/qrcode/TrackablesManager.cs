using Meta.XR.MRUtilityKit;
using System.Collections.Generic;
using UnityEngine;

public class TrackablesManager : MonoBehaviour
{
    [SerializeField] private GameObject spawnSpherePrefab;
    [SerializeField] private GameObject spawnCubePrefab;
    [SerializeField] private GameObject machineVisualizationPrefab;
    [SerializeField] private GameObject feedbackPrefab;
    [SerializeField] private bool enableTestMode;
    [SerializeField] private List<string> testQrPayloads = new();
    [SerializeField] private float testMachineSpacing = 1.5f;

    private readonly Dictionary<string, MachineVisualizationContext> machineVisualizations = new();
    private readonly Dictionary<string, ServerDataPayload> pendingPayloads = new();
    private readonly object payloadLock = new();

    private ServerConnectionClient serverConnectionClient;

    private void Awake()
    {
        serverConnectionClient = new ServerConnectionClient();
        serverConnectionClient.PayloadReceived += OnServerPayloadReceived;
    }

    private void Start()
    {
        if (enableTestMode)
        {
            ConnectTestPayloads();
        }
    }

    private void OnDestroy()
    {
        if (serverConnectionClient != null)
        {
            serverConnectionClient.PayloadReceived -= OnServerPayloadReceived;
            serverConnectionClient.Dispose();
        }
    }

    private void OnServerPayloadReceived(ServerConnection connection, ServerDataPayload payload)
    {
        lock (payloadLock)
        {
            pendingPayloads[connection.Endpoint] = payload;
        }
    }

    public async void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
        {
            return;
        }

        string qrPayload = trackable.MarkerPayloadString;
        if (!ServerConnectionClient.TryParseConnectionInfo(qrPayload, out QrConnectionInfo connectionInfo))
        {
            Debug.LogWarning("Scanned QR payload is not an InfraSight connection payload.");
            return;
        }

        string endpoint = ServerConnectionClient.BuildEndpoint(connectionInfo);
        if (machineVisualizations.ContainsKey(endpoint))
        {
            return;
        }

        GameObject feedbackObject = CreateFeedback(trackable.transform);
        ServerConnection connection = await serverConnectionClient.ConnectToServerAsync(qrPayload);
        DestroyFeedback(feedbackObject);

        if (connection == null)
        {
            Debug.LogWarning("Did not connect from this QR payload. It may not be a server connection QR code.");
            return;
        }

        if (!machineVisualizations.ContainsKey(connection.Endpoint))
        {
            CreateMachineVisualization(connection, trackable.transform.position, trackable.transform.rotation);
        }
    }

    public void OnTrackableRemoved(MRUKTrackable trackable)
    {
        // Connections and machine views persist after QR tracking is lost.
    }

    public async void ConnectTestPayloads()
    {
        for (int i = 0; i < testQrPayloads.Count; i++)
        {
            string qrPayload = testQrPayloads[i];
            ServerConnection connection = await serverConnectionClient.ConnectToServerAsync(qrPayload);
            if (connection == null || machineVisualizations.ContainsKey(connection.Endpoint))
            {
                continue;
            }

            Vector3 position = transform.position + new Vector3(i * testMachineSpacing, 0f, 0f);
            CreateMachineVisualization(connection, position, transform.rotation);
        }
    }

    private void Update()
    {
        Dictionary<string, ServerDataPayload> payloadsToApply = null;
        lock (payloadLock)
        {
            if (pendingPayloads.Count > 0)
            {
                payloadsToApply = new Dictionary<string, ServerDataPayload>(pendingPayloads);
                pendingPayloads.Clear();
            }
        }

        if (payloadsToApply == null)
        {
            return;
        }

        foreach (KeyValuePair<string, ServerDataPayload> entry in payloadsToApply)
        {
            if (machineVisualizations.TryGetValue(entry.Key, out MachineVisualizationContext context))
            {
                ApplyPayload(context, entry.Value);
            }
        }
    }

    private GameObject CreateFeedback(Transform parent)
    {
        if (feedbackPrefab == null)
        {
            return null;
        }

        GameObject feedbackObject = Instantiate(feedbackPrefab, parent);
        feedbackObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        if (feedbackObject.TryGetComponent<QRTracker>(out QRTracker feedbackTracker))
        {
            feedbackTracker.qrID = "CONNECTING";
        }

        return feedbackObject;
    }

    private static void DestroyFeedback(GameObject feedbackObject)
    {
        if (feedbackObject != null)
        {
            Destroy(feedbackObject);
        }
    }

    private void CreateMachineVisualization(ServerConnection connection, Vector3 position, Quaternion rotation)
    {
        GameObject prefabToSpawn = machineVisualizationPrefab != null ? machineVisualizationPrefab : spawnSpherePrefab;
        if (prefabToSpawn == null)
        {
            Debug.LogWarning("No machine visualization prefab configured.");
            return;
        }

        GameObject root = Instantiate(prefabToSpawn, transform);
        root.transform.SetPositionAndRotation(position, rotation * Quaternion.Euler(0f, 180f, 0f));

        if (root.TryGetComponent<QRTracker>(out QRTracker tracker))
        {
            tracker.qrID = connection.MachineName;
        }

        machineVisualizations[connection.Endpoint] = new MachineVisualizationContext(root);
    }

    private void ApplyPayload(MachineVisualizationContext context, ServerDataPayload payload)
    {
        if (context.Root == null || payload?.machine == null)
        {
            return;
        }

        float normalizedCpu = Mathf.Clamp01(payload.machine.cpu / 100f);
        float normalizedRam = Mathf.Clamp01(payload.machine.ram / 100f);

        if (context.Root.TryGetComponent<MachineVisualization>(out MachineVisualization machineVis))
        {
            machineVis.UpdateMachineChart(payload);
        }
        else
        {
            float scale = Mathf.Lerp(0.4f, 1.4f, normalizedCpu);
            context.Root.transform.localScale = new Vector3(scale, scale, scale);

            Renderer renderer = context.Root.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = Color.Lerp(Color.green, Color.red, normalizedRam);
            }
        }

        ApplyContainers(context, payload.containers);
    }

    private void ApplyContainers(MachineVisualizationContext context, ContainerDataPayload[] containers)
    {
        HashSet<string> activeContainerIds = new();
        if (containers != null)
        {
            for (int i = 0; i < containers.Length; i++)
            {
                ContainerDataPayload containerData = containers[i];
                if (containerData == null || string.IsNullOrWhiteSpace(containerData.id))
                {
                    continue;
                }

                activeContainerIds.Add(containerData.id);
                if (!context.ContainerVisualizations.TryGetValue(containerData.id, out GameObject containerGo) || containerGo == null)
                {
                    if (spawnCubePrefab == null)
                    {
                        continue;
                    }

                    containerGo = Instantiate(spawnCubePrefab, context.Root.transform.parent);
                    context.ContainerVisualizations[containerData.id] = containerGo;
                }

                float angle = (360f / Mathf.Max(1, containers.Length)) * i;
                float radius = 0.8f;
                Vector3 offset = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0.2f, Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
                containerGo.transform.SetLocalPositionAndRotation(context.Root.transform.localPosition + offset, Quaternion.identity);
                containerGo.transform.localScale = Vector3.one * 0.2f;

                Renderer containerRenderer = containerGo.GetComponentInChildren<Renderer>();
                if (containerRenderer != null)
                {
                    containerRenderer.material.color = containerData.status == "running" ? Color.green : Color.red;
                }
            }
        }

        List<string> staleIds = new();
        foreach (string containerId in context.ContainerVisualizations.Keys)
        {
            if (!activeContainerIds.Contains(containerId))
            {
                staleIds.Add(containerId);
            }
        }

        foreach (string staleId in staleIds)
        {
            GameObject staleObject = context.ContainerVisualizations[staleId];
            if (staleObject != null)
            {
                Destroy(staleObject);
            }

            context.ContainerVisualizations.Remove(staleId);
        }
    }

    private sealed class MachineVisualizationContext
    {
        public GameObject Root { get; }
        public Dictionary<string, GameObject> ContainerVisualizations { get; } = new();

        public MachineVisualizationContext(GameObject root)
        {
            Root = root;
        }
    }
}
