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

public class AndroidArQrScanProvider : QrScanProviderBehaviour
{
    [SerializeField] private ARCameraManager cameraManager;
    [SerializeField] private ARTrackedImageManager trackedImageManager;
    [SerializeField] private float scanIntervalSeconds = 0.5f;
    [SerializeField] private float duplicatePayloadCooldownSeconds = 5f;
    [SerializeField] private int downsampleWidth = 640;
    [SerializeField] private float fallbackPoseDistanceMeters = 1f;

    private readonly BarcodeReaderGeneric barcodeReader = new()
    {
        AutoRotate = true,
        TryInverted = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            PossibleFormats = new[] { BarcodeFormat.QR_CODE }
        }
    };

    private readonly Dictionary<string, string> trackedPayloadsByImageName = new();
    private readonly HashSet<string> requestedTrackedPayloads = new();
    private readonly HashSet<TrackableId> activeTrackedImages = new();
    private MutableRuntimeReferenceImageLibrary mutableImageLibrary;
    private Coroutine scanCoroutine;
    private Matrix4x4? displayMatrix;
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
        if (cameraManager != null)
        {
            cameraManager.frameReceived += OnCameraFrameReceived;
        }

        SubscribeTrackedImageEvents();
    }

    private void OnDisable()
    {
        if (cameraManager != null)
        {
            cameraManager.frameReceived -= OnCameraFrameReceived;
        }

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

            RequestOfficialImageTracking(result.Text);
            if (IsDuplicatePayloadOnCooldown(result.Text)
                || !TryGetQrScreenPoint(result, width, height, out Vector2 screenPoint)
                || !TryGetFallbackPose(screenPoint, out Pose fallbackPose))
            {
                return;
            }

            RecordDetectedPayload(result.Text);
            RaiseQrDetected(result.Text, fallbackPose, false);
        }
    }

    private void ResolveManagers()
    {
        cameraManager ??= FindFirstObjectByType<ARCameraManager>();
        trackedImageManager ??= FindFirstObjectByType<ARTrackedImageManager>(FindObjectsInactive.Include);
    }

    private void RequestOfficialImageTracking(string payload)
    {
        if (!TryGetOfficialQrPngUrl(payload, out string imageUrl, out string endpoint)
            || !requestedTrackedPayloads.Add(payload))
        {
            return;
        }

        StartCoroutine(AddOfficialQrImage(payload, endpoint, imageUrl));
    }

    private static bool TryGetOfficialQrPngUrl(string payload, out string imageUrl, out string endpoint)
    {
        imageUrl = null;
        endpoint = null;
        if (string.IsNullOrWhiteSpace(payload)
            || payload.IndexOf("\"name\"", StringComparison.OrdinalIgnoreCase) >= 0
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
                Debug.LogWarning($"ARCore image tracking unavailable; retaining provisional QR pose for endpoint {endpoint}.");
                yield break;
            }

            yield return null;
        }

        if (!TryInitializeImageTracking())
        {
            Debug.LogWarning($"Could not initialize ARCore image tracking; retaining provisional QR pose for endpoint {endpoint}.");
            yield break;
        }

        Debug.Log($"Downloading official spatial QR image for endpoint {endpoint} from {imageUrl}.");
        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl, false);
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"Could not download spatial QR image for endpoint {endpoint}: {request.error}. Retaining provisional pose.");
            yield break;
        }

        Texture2D texture = DownloadHandlerTexture.GetContent(request);
        string imageName = $"InfraSight QR {endpoint}";
        AddReferenceImageJobState addState;
        try
        {
            addState = mutableImageLibrary.ScheduleAddImageWithValidationJob(texture, imageName, null);
        }
        catch (Exception exception)
        {
            Destroy(texture);
            Debug.LogWarning($"Could not submit spatial QR image for endpoint {endpoint}: {exception.Message}. Retaining provisional pose.");
            yield break;
        }

        while (!addState.status.IsComplete())
        {
            yield return null;
        }

        Destroy(texture);
        if (!addState.status.IsSuccess())
        {
            Debug.LogWarning($"ARCore rejected spatial QR image for endpoint {endpoint}; retaining provisional pose.");
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

        if (activeTrackedImages.Add(trackedImage.trackableId))
        {
            Debug.Log($"Tracking official spatial QR transform for {trackedImage.referenceImage.name}.");
        }

        RaiseQrDetected(
            payload,
            new Pose(trackedImage.transform.position, trackedImage.transform.rotation),
            true);
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        if (args.displayMatrix.HasValue)
        {
            displayMatrix = args.displayMatrix.Value;
        }
    }

    private bool TryGetQrScreenPoint(Result result, int imageWidth, int imageHeight, out Vector2 screenPoint)
    {
        screenPoint = default;
        if (!displayMatrix.HasValue
            || result.ResultPoints == null
            || result.ResultPoints.Length == 0)
        {
            return false;
        }

        Vector2 center = Vector2.zero;
        for (int i = 0; i < result.ResultPoints.Length; i++)
        {
            center += new Vector2(result.ResultPoints[i].X, result.ResultPoints[i].Y);
        }

        center /= result.ResultPoints.Length;

        // ZXing reads the MirrorY-converted image, so reverse X before applying
        // AR Foundation's native-camera-texture to display-space transform.
        var nativeTexturePoint = new Vector4(
            1f - Mathf.Clamp01(center.x / Mathf.Max(1, imageWidth - 1)),
            Mathf.Clamp01(center.y / Mathf.Max(1, imageHeight - 1)),
            1f,
            0f);
        Vector4 viewportPoint = displayMatrix.Value.inverse.transpose * nativeTexturePoint;
        screenPoint = new Vector2(viewportPoint.x * Screen.width, viewportPoint.y * Screen.height);
        return true;
    }

    private bool TryGetFallbackPose(Vector2 screenPoint, out Pose pose)
    {
        pose = default;
        Camera arCamera = cameraManager != null ? cameraManager.GetComponent<Camera>() : null;
        if (arCamera == null)
        {
            return false;
        }

        Ray ray = arCamera.ScreenPointToRay(screenPoint);
        pose = new Pose(
            ray.GetPoint(Mathf.Max(0.1f, fallbackPoseDistanceMeters)),
            cameraManager.transform.rotation);
        return true;
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
