using System.Collections.Generic;
using UnityEngine;

public class InfraSightQrClient : MonoBehaviour
{
    [SerializeField] private QrScanProviderBehaviour qrScanProvider;
    [SerializeField] private GameObject spawnCubePrefab;
    [SerializeField] private GameObject machineVisualizationPrefab;
    [SerializeField] private GameObject feedbackPrefab;
    [SerializeField] private bool enableTestMode;
#if UNITY_EDITOR
    [SerializeField] private bool enableEditorTestMode = true;
    [SerializeField] private Vector3 editorTestAnchorOffset = new(0f, -0.15f, 1.25f);
#endif
    [SerializeField] private List<string> testQrPayloads = new();
    [SerializeField] private float testMachineSpacing = 1.5f;

    private InfraSightConnectionOrchestrator orchestrator;
    private InfraSightMachineVisualizationManager visualizationManager;

    private void Awake()
    {
        LoadDefaultPrefabs();
        visualizationManager = new InfraSightMachineVisualizationManager(
            transform,
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

        if (ShouldConnectTestPayloads())
        {
            Debug.Log($"InfraSight QR test mode connecting {testQrPayloads.Count} payload(s).");
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
            Pose pose = CreateTestPose(i);
            orchestrator.ConnectQrPayload(testQrPayloads[i], pose);
        }
    }

    private void OnQrDetected(QrScanResult result)
    {
        orchestrator.ConnectQrPayload(result.Payload, result.Pose, result.HasTrackedPose);
    }

    private void LoadDefaultPrefabs()
    {
        spawnCubePrefab ??= Resources.Load<GameObject>("InfraSight/Cube");
        feedbackPrefab ??= Resources.Load<GameObject>("InfraSight/FeedBack");
        machineVisualizationPrefab ??= Resources.Load<GameObject>("InfraSight/MachineInfo");
    }

    private bool ShouldConnectTestPayloads()
    {
        if (enableTestMode)
        {
            return true;
        }

#if UNITY_EDITOR
        return enableEditorTestMode;
#else
        return false;
#endif
    }

    private Pose CreateTestPose(int payloadIndex)
    {
#if UNITY_EDITOR
        if (Application.isEditor && Camera.main != null)
        {
            Transform cameraTransform = Camera.main.transform;
            Vector3 position = cameraTransform.TransformPoint(
                editorTestAnchorOffset + new Vector3(payloadIndex * testMachineSpacing, 0f, 0f));
            Quaternion rotation = Quaternion.LookRotation(cameraTransform.forward, Vector3.up);
            return new Pose(position, rotation);
        }
#endif

        Vector3 fallbackPosition = transform.position + new Vector3(payloadIndex * testMachineSpacing, 0f, 0f);
        return new Pose(fallbackPosition, transform.rotation);
    }
}
