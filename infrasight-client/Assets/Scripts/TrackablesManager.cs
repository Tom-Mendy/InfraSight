using System;
using System.Collections.Generic;
using UnityEngine;

public class TrackablesManager : MonoBehaviour
{
    [Header("Spawn Prefabs")]
    [SerializeField] private GameObject spawnSpherePrefab;
    [SerializeField] private GameObject spawnCubePrefab;

    [Header("Providers")]
    [SerializeField] private MonoBehaviour questProviderBehaviour;
    [SerializeField] private MonoBehaviour androidProviderBehaviour;

    private readonly Dictionary<string, GameObject> _spawnedByTrackableId = new();
    private ITrackableProvider _activeProvider;

    private void Awake()
    {
        ResolveProviders();
        _activeProvider = ChooseActiveProvider();

        if (_activeProvider == null)
        {
            Debug.LogWarning("No trackable provider available. QR spawning is disabled on this platform.");
            return;
        }

        _activeProvider.TrackableAdded += HandleTrackableAdded;
        _activeProvider.TrackableRemoved += HandleTrackableRemoved;
        _activeProvider.StartProvider();

        Debug.Log($"TrackablesManager bound to provider: {_activeProvider.GetType().Name}");
    }

    private void OnDestroy()
    {
        if (_activeProvider == null)
        {
            return;
        }

        _activeProvider.TrackableAdded -= HandleTrackableAdded;
        _activeProvider.TrackableRemoved -= HandleTrackableRemoved;
        _activeProvider.StopProvider();
    }

    private void ResolveProviders()
    {
        if (questProviderBehaviour == null)
        {
            questProviderBehaviour = GetComponent<QuestTrackableProvider>();
        }

        if (androidProviderBehaviour == null)
        {
            androidProviderBehaviour = GetComponent<AndroidArCoreTrackableProvider>();
        }
    }

    private ITrackableProvider ChooseActiveProvider()
    {
        ITrackableProvider questProvider = questProviderBehaviour as ITrackableProvider;
        ITrackableProvider androidProvider = androidProviderBehaviour as ITrackableProvider;

        bool isAndroid = Application.platform == RuntimePlatform.Android;
        bool likelyQuest = isAndroid && IsLikelyQuestDevice();

        if (likelyQuest && questProvider != null && questProvider.IsSupported)
        {
            return questProvider;
        }

        if (isAndroid && androidProvider != null && androidProvider.IsSupported)
        {
            return androidProvider;
        }

        if (questProvider != null && questProvider.IsSupported)
        {
            return questProvider;
        }

        if (androidProvider != null && androidProvider.IsSupported)
        {
            return androidProvider;
        }

        return null;
    }

    private static bool IsLikelyQuestDevice()
    {
        string model = (SystemInfo.deviceModel ?? string.Empty).ToLowerInvariant();
        string deviceName = (SystemInfo.deviceName ?? string.Empty).ToLowerInvariant();

        return model.Contains("quest") || deviceName.Contains("quest") || model.Contains("hollywood");
    }

    private void HandleTrackableAdded(TrackableData trackableData)
    {
        if (trackableData.Kind != TrackableKind.QRCode)
        {
            return;
        }

        string qrId = trackableData.PayloadId;
        Debug.Log($"I see a {qrId} ({trackableData.TrackableId})");

        if (_spawnedByTrackableId.TryGetValue(trackableData.TrackableId, out GameObject existing))
        {
            Destroy(existing);
            _spawnedByTrackableId.Remove(trackableData.TrackableId);
        }

        GameObject prefabToSpawn = ResolvePrefab(qrId);
        if (prefabToSpawn == null)
        {
            Debug.LogWarning($"No prefab configured for QR payload '{qrId}'.");
            return;
        }

        Transform parent = trackableData.ParentTransform != null ? trackableData.ParentTransform : transform;
        GameObject go = Instantiate(prefabToSpawn, parent);

        if (trackableData.ParentTransform != null)
        {
            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
        else
        {
            go.transform.SetPositionAndRotation(trackableData.Pose.position, trackableData.Pose.rotation);
        }

        QRTracker tracker = go.GetComponent<QRTracker>();
        if (tracker != null)
        {
            tracker.qrID = qrId;
        }

        _spawnedByTrackableId[trackableData.TrackableId] = go;
    }

    private void HandleTrackableRemoved(TrackableData trackableData)
    {
        if (!_spawnedByTrackableId.TryGetValue(trackableData.TrackableId, out GameObject spawned))
        {
            return;
        }

        Destroy(spawned);
        _spawnedByTrackableId.Remove(trackableData.TrackableId);
    }

    private GameObject ResolvePrefab(string qrId)
    {
        return qrId switch
        {
            "QR_Sphere" => spawnSpherePrefab,
            "QR_Cube" => spawnCubePrefab,
            _ => spawnSpherePrefab,
        };
    }
}