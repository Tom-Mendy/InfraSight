using System.Collections.Generic;
using UnityEngine;

#if INFRASIGHT_META_QUEST || UNITY_META_QUEST
using Meta.XR.MRUtilityKit;
#endif

public class TrackablesManager : MonoBehaviour
{
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
        visualizationManager = new InfraSightMachineVisualizationManager(
            transform,
            spawnSpherePrefab,
            spawnCubePrefab,
            machineVisualizationPrefab,
            feedbackPrefab);
        orchestrator = new InfraSightConnectionOrchestrator(visualizationManager);
    }

    private void Start()
    {
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
        orchestrator?.Dispose();
    }

#if INFRASIGHT_META_QUEST || UNITY_META_QUEST
    public void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable == null || trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
        {
            return;
        }

        var pose = new Pose(trackable.transform.position, trackable.transform.rotation);
        orchestrator.ConnectQrPayload(trackable.MarkerPayloadString, pose);
    }

    public void OnTrackableRemoved(MRUKTrackable trackable)
    {
        // Connections and machine views persist after QR tracking is lost.
    }
#else
    public void OnTrackableRemoved(Object trackable)
    {
        // Connections and machine views persist after QR tracking is lost.
    }
#endif

    public void ConnectTestPayloads()
    {
        for (int i = 0; i < testQrPayloads.Count; i++)
        {
            Vector3 position = transform.position + new Vector3(i * testMachineSpacing, 0f, 0f);
            var pose = new Pose(position, transform.rotation);
            orchestrator.ConnectQrPayload(testQrPayloads[i], pose);
        }
    }
}
