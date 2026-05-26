using System.Collections.Generic;
using UnityEngine;

public class InfraSightQrClient : MonoBehaviour
{
    [SerializeField] private QrScanProviderBehaviour qrScanProvider;
    [SerializeField] private GameObject spawnSpherePrefab;
    [SerializeField] private GameObject spawnCubePrefab;
    [SerializeField] private GameObject machineVisualizationPrefab;
    [SerializeField] private GameObject feedbackPrefab;
    [SerializeField] private bool enableTestMode;
    [SerializeField] private List<string> testQrPayloads = new();
    [SerializeField] private float testMachineSpacing = 1.5f;

    private InfraSightConnectionOrchestrator orchestrator;
    private InfraSightMachineVisualizationManager visualizationManager;

    private void Awake()
    {
        LoadDefaultPrefabs();
        visualizationManager = new InfraSightMachineVisualizationManager(
            transform,
            spawnSpherePrefab,
            spawnCubePrefab,
            machineVisualizationPrefab,
            feedbackPrefab);
        orchestrator = new InfraSightConnectionOrchestrator(visualizationManager);

        if (qrScanProvider == null)
        {
            qrScanProvider = GetComponent<QrScanProviderBehaviour>();
        }

        if (qrScanProvider != null)
        {
            qrScanProvider.QrDetected += OnQrDetected;
        }
    }

    private void Start()
    {
        if (qrScanProvider != null && qrScanProvider.IsSupported)
        {
            qrScanProvider.StartScanning();
        }

        if (enableTestMode)
        {
            ConnectTestPayloads();
        }
    }

    private void Update()
    {
        orchestrator?.ApplyPendingPayloads();
    }

    private void OnDestroy()
    {
        if (qrScanProvider != null)
        {
            qrScanProvider.QrDetected -= OnQrDetected;
            qrScanProvider.StopScanning();
        }

        orchestrator?.Dispose();
    }

    public void ConnectTestPayloads()
    {
        for (int i = 0; i < testQrPayloads.Count; i++)
        {
            Vector3 position = transform.position + new Vector3(i * testMachineSpacing, 0f, 0f);
            orchestrator.ConnectQrPayload(testQrPayloads[i], new Pose(position, transform.rotation));
        }
    }

    private void OnQrDetected(QrScanResult result)
    {
        orchestrator.ConnectQrPayload(result.Payload, result.Pose, result.HasTrackedPose);
    }

    private void LoadDefaultPrefabs()
    {
        spawnSpherePrefab ??= Resources.Load<GameObject>("InfraSight/Sphere");
        spawnCubePrefab ??= Resources.Load<GameObject>("InfraSight/Cube");
        feedbackPrefab ??= Resources.Load<GameObject>("InfraSight/FeedBack");
        machineVisualizationPrefab ??= Resources.Load<GameObject>("InfraSight/MachineInfo");
    }
}
