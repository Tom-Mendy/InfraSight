#if INFRASIGHT_ANDROID_AR
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ZXing;
using ZXing.Common;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class AndroidArQrScanProvider : QrScanProviderBehaviour
{
    [SerializeField] private ARCameraManager cameraManager;
    [SerializeField] private ARTrackedImageManager trackedImageManager;
    [SerializeField] private float scanIntervalSeconds = 0.5f;
    [SerializeField] private float duplicatePayloadCooldownSeconds = 5f;
    [SerializeField] private int downsampleWidth = 640;
    [SerializeField] private float trackedQrPhysicalWidthMeters = 0.16f;
    [SerializeField] private float trackedQrForwardOffsetMeters = 0.12f;

    private readonly BarcodeReaderGeneric barcodeReader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            TryInverted = true,
            PossibleFormats = new[] { BarcodeFormat.QR_CODE }
        }
    };

    private readonly Dictionary<string, string> trackedPayloadsByImageName = new();
    private readonly HashSet<string> requestedTrackedPayloads = new();
    private readonly HashSet<TrackableId> activeTrackedImages = new();
    private MutableRuntimeReferenceImageLibrary mutableImageLibrary;
    private Coroutine scanCoroutine;
    private string lastPayload;
    private float lastPayloadTime = float.NegativeInfinity;
    private bool trackingEventsSubscribed;

    public override bool IsSupported => cameraManager != null;

    private void Awake()
    {
        ResolveManagers();
    }

    private void OnEnable()
    {
        ResolveManagers();
        SubscribeTrackedImageEvents();
    }

    private void OnDisable()
    {
        UnsubscribeTrackedImageEvents();
    }

    public override void StartScanning()
    {
        ResolveManagers();
        base.StartScanning();
        if (scanCoroutine == null && IsSupported)
        {
            scanCoroutine = StartCoroutine(ScanLoop());
        }
    }

    public override void StopScanning()
    {
        if (scanCoroutine != null)
        {
            StopCoroutine(scanCoroutine);
            scanCoroutine = null;
        }

        base.StopScanning();
    }

    private IEnumerator ScanLoop()
    {
        var wait = new WaitForSeconds(scanIntervalSeconds);
        while (enabled)
        {
            TryScanFrame();
            yield return wait;
        }
    }

    private void TryScanFrame()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            return;
        }
#endif

        if (cameraManager == null || !cameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
        {
            return;
        }

        using (cpuImage)
        {
            int width = downsampleWidth > 0 ? Mathf.Min(downsampleWidth, cpuImage.width) : cpuImage.width;
            int height = Mathf.Max(1, Mathf.RoundToInt(cpuImage.height * (width / (float)cpuImage.width)));
            var conversionParams = new XRCpuImage.ConversionParams(
                cpuImage,
                TextureFormat.RGBA32,
                XRCpuImage.Transformation.MirrorY);

            conversionParams.outputDimensions = new Vector2Int(width, height);
            int size = cpuImage.GetConvertedDataSize(conversionParams);
            using NativeArray<byte> buffer = new(size, Allocator.Temp);
            cpuImage.Convert(conversionParams, buffer);

            byte[] pixels = buffer.ToArray();
            Result result = barcodeReader.Decode(pixels, width, height, RGBLuminanceSource.BitmapFormat.RGBA32);
            if (result == null)
            {
                return;
            }

            if (!TryGetOfficialQrPngUrl(result.Text, out string imageUrl, out string endpoint))
            {
                if (!IsDuplicatePayloadOnCooldown(result.Text))
                {
                    RecordDetectedPayload(result.Text);
                    Debug.LogWarning("Scanned QR payload is not an InfraSight connection payload; ignoring for tracked anchoring.");
                }

                return;
            }

            bool queuedTracking = RequestOfficialImageTracking(result.Text, endpoint, imageUrl);
            if (IsDuplicatePayloadOnCooldown(result.Text))
            {
                return;
            }

            RecordDetectedPayload(result.Text);
            Debug.Log(
                queuedTracking
                    ? $"Detected InfraSight QR for endpoint {endpoint}; waiting for ARCore tracked-image pose before anchoring."
                    : $"Detected InfraSight QR for endpoint {endpoint}; ARCore tracked-image registration already pending.");
        }
    }

    private void ResolveManagers()
    {
        cameraManager ??= FindFirstObjectByType<ARCameraManager>();
        trackedImageManager ??= FindFirstObjectByType<ARTrackedImageManager>(FindObjectsInactive.Include);
    }

    private bool RequestOfficialImageTracking(string payload, string endpoint, string imageUrl)
    {
        if (!requestedTrackedPayloads.Add(payload))
        {
            return false;
        }

        StartCoroutine(AddOfficialQrImage(payload, endpoint, imageUrl));
        return true;
    }

    private static bool TryGetOfficialQrPngUrl(string payload, out string imageUrl, out string endpoint)
    {
        imageUrl = null;
        endpoint = null;
        if (string.IsNullOrWhiteSpace(payload)
            || !ServerConnectionClient.TryParseConnectionInfo(payload, out QrConnectionInfo connectionInfo))
        {
            return false;
        }

        endpoint = ServerConnectionClient.BuildEndpoint(connectionInfo);
        string scheme = endpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        imageUrl = $"{scheme}://{connectionInfo.ip}:{connectionInfo.port}/qr.png";
        return true;
    }

    private IEnumerator AddOfficialQrImage(string payload, string endpoint, string imageUrl)
    {
        while (!IsSessionReadyForImageTracking())
        {
            if (ARSession.state == ARSessionState.Unsupported)
            {
                Debug.LogWarning($"ARCore image tracking unavailable; cannot anchor endpoint {endpoint} from QR tracker.");
                yield break;
            }

            yield return null;
        }

        if (!TryInitializeImageTracking())
        {
            requestedTrackedPayloads.Remove(payload);
            Debug.LogWarning($"Could not initialize ARCore image tracking; cannot anchor endpoint {endpoint} from QR tracker.");
            yield break;
        }

        Debug.Log($"Downloading official spatial QR image for endpoint {endpoint} from {imageUrl}.");
        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl, false);
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            requestedTrackedPayloads.Remove(payload);
            Debug.LogWarning($"Could not download spatial QR image for endpoint {endpoint}: {request.error}. Cannot anchor from QR tracker.");
            yield break;
        }

        Texture2D texture = DownloadHandlerTexture.GetContent(request);
        string imageName = $"InfraSight QR {endpoint}";
        AddReferenceImageJobState addState;
        try
        {
            addState = mutableImageLibrary.ScheduleAddImageWithValidationJob(
                texture,
                imageName,
                Mathf.Max(0.01f, trackedQrPhysicalWidthMeters));
        }
        catch (Exception exception)
        {
            Destroy(texture);
            requestedTrackedPayloads.Remove(payload);
            Debug.LogWarning($"Could not submit spatial QR image for endpoint {endpoint}: {exception.Message}. Cannot anchor from QR tracker.");
            yield break;
        }

        while (!addState.status.IsComplete())
        {
            yield return null;
        }

        Destroy(texture);
        if (!addState.status.IsSuccess())
        {
            requestedTrackedPayloads.Remove(payload);
            Debug.LogWarning($"ARCore rejected spatial QR image for endpoint {endpoint}; cannot anchor from QR tracker.");
            yield break;
        }

        trackedPayloadsByImageName[imageName] = payload;
        Debug.Log($"Registered official spatial QR image for endpoint {endpoint}; waiting for tracked transform.");
    }

    private static bool IsSessionReadyForImageTracking()
    {
        return ARSession.state == ARSessionState.Ready
            || ARSession.state == ARSessionState.SessionInitializing
            || ARSession.state == ARSessionState.SessionTracking;
    }

    private bool TryInitializeImageTracking()
    {
        ResolveManagers();
        if (mutableImageLibrary != null)
        {
            SubscribeTrackedImageEvents();
            return true;
        }

        if (trackedImageManager == null)
        {
            return false;
        }

        try
        {
            trackedImageManager.enabled = false;
            mutableImageLibrary = trackedImageManager.CreateRuntimeLibrary() as MutableRuntimeReferenceImageLibrary;
            if (mutableImageLibrary == null)
            {
                return false;
            }

            trackedImageManager.referenceLibrary = mutableImageLibrary;
            trackedImageManager.requestedMaxNumberOfMovingImages = 4;
            SubscribeTrackedImageEvents();
            trackedImageManager.enabled = true;
            Debug.Log("Initialized ARCore tracked-image support for official spatial QR codes.");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"ARCore tracked-image initialization failed: {exception.Message}");
            return false;
        }
    }

    private void SubscribeTrackedImageEvents()
    {
        if (trackedImageManager != null && !trackingEventsSubscribed)
        {
            trackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
            trackingEventsSubscribed = true;
        }
    }

    private void UnsubscribeTrackedImageEvents()
    {
        if (trackedImageManager != null && trackingEventsSubscribed)
        {
            trackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
            trackingEventsSubscribed = false;
        }
    }

    private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
    {
        foreach (ARTrackedImage trackedImage in eventArgs.added)
        {
            EmitTrackedPose(trackedImage);
        }

        foreach (ARTrackedImage trackedImage in eventArgs.updated)
        {
            EmitTrackedPose(trackedImage);
        }
    }

    private void EmitTrackedPose(ARTrackedImage trackedImage)
    {
        if (trackedImage.trackingState != TrackingState.Tracking
            || !trackedPayloadsByImageName.TryGetValue(trackedImage.referenceImage.name, out string payload))
        {
            return;
        }

        Pose anchorPose = BuildAnchorPose(trackedImage);
        if (activeTrackedImages.Add(trackedImage.trackableId))
        {
            Debug.Log(
                $"Tracking official spatial QR transform for {trackedImage.referenceImage.name}: " +
                $"center={trackedImage.transform.position}, " +
                $"anchor={anchorPose.position}, " +
                $"width={Mathf.Max(0.01f, trackedQrPhysicalWidthMeters):0.###}m, " +
                $"offset={Mathf.Max(0f, trackedQrForwardOffsetMeters):0.###}m.");
        }

        RaiseQrDetected(
            payload,
            anchorPose,
            true);
    }

    private Pose BuildAnchorPose(ARTrackedImage trackedImage)
    {
        Vector3 center = trackedImage.transform.position;
        Vector3 toCamera = GetDirectionToCamera(center, trackedImage.transform.forward);
        Vector3 anchorPosition = center + toCamera * Mathf.Max(0f, trackedQrForwardOffsetMeters);
        Quaternion anchorRotation = Quaternion.LookRotation(-toCamera, Vector3.up);
        return new Pose(anchorPosition, anchorRotation);
    }

    private Vector3 GetDirectionToCamera(Vector3 fromPosition, Vector3 fallbackDirection)
    {
        Camera arCamera = cameraManager != null ? cameraManager.GetComponent<Camera>() : null;
        if (arCamera != null)
        {
            Vector3 toCamera = arCamera.transform.position - fromPosition;
            if (toCamera.sqrMagnitude > 0.0001f)
            {
                return toCamera.normalized;
            }
        }

        return fallbackDirection.sqrMagnitude > 0.0001f
            ? fallbackDirection.normalized
            : Vector3.forward;
    }

    private void RecordDetectedPayload(string payload)
    {
        lastPayload = payload;
        lastPayloadTime = Time.unscaledTime;
    }

    private bool IsDuplicatePayloadOnCooldown(string payload)
    {
        return !string.IsNullOrWhiteSpace(payload)
            && payload == lastPayload
            && Time.unscaledTime - lastPayloadTime < duplicatePayloadCooldownSeconds;
    }
}
#else
using UnityEngine;

public class AndroidArQrScanProvider : QrScanProviderBehaviour
{
    public override bool IsSupported => false;

    public override void StartScanning()
    {
        Debug.LogWarning("Android AR QR scanning requires INFRASIGHT_ANDROID_AR, AR Foundation/ARCore, and ZXing.");
    }
}
#endif
