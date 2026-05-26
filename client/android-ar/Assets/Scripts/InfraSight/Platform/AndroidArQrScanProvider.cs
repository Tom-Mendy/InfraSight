#if INFRASIGHT_ANDROID_AR
using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ZXing;
using ZXing.Common;

public class AndroidArQrScanProvider : QrScanProviderBehaviour
{
    [SerializeField] private ARCameraManager cameraManager;
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private float scanIntervalSeconds = 0.5f;
    [SerializeField] private float duplicatePayloadCooldownSeconds = 5f;
    [SerializeField] private int downsampleWidth = 640;
    [SerializeField] private float fallbackPoseDistanceMeters = 1f;

    private readonly BarcodeReaderGeneric barcodeReader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            PossibleFormats = new[] { BarcodeFormat.QR_CODE }
        }
    };

    private readonly System.Collections.Generic.List<ARRaycastHit> raycastHits = new();
    private Coroutine scanCoroutine;
    private Matrix4x4? displayMatrix;
    private string lastPayload;
    private float lastPayloadTime = float.NegativeInfinity;
    private bool lastPayloadHasTrackedPose;

    public override bool IsSupported => cameraManager != null && raycastManager != null;

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
    }

    private void OnDisable()
    {
        if (cameraManager != null)
        {
            cameraManager.frameReceived -= OnCameraFrameReceived;
        }
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

            bool isDuplicatePayloadOnCooldown = IsDuplicatePayloadOnCooldown(result.Text);
            if (!TryGetQrScreenPoint(result, width, height, out Vector2 screenPoint))
            {
                return;
            }

            if (TryGetQrSurfacePose(screenPoint, out Pose trackedPose))
            {
                if (isDuplicatePayloadOnCooldown && lastPayloadHasTrackedPose)
                {
                    return;
                }

                RecordDetectedPayload(result.Text, true);
                RaiseQrDetected(result.Text, trackedPose, true);
                return;
            }

            if (isDuplicatePayloadOnCooldown || !TryGetFallbackPose(screenPoint, out Pose fallbackPose))
            {
                return;
            }

            RecordDetectedPayload(result.Text, false);
            RaiseQrDetected(result.Text, fallbackPose, false);
        }
    }

    private void ResolveManagers()
    {
        cameraManager ??= FindFirstObjectByType<ARCameraManager>();
        raycastManager ??= FindFirstObjectByType<ARRaycastManager>();
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

    private bool TryGetQrSurfacePose(Vector2 screenPoint, out Pose pose)
    {
        pose = default;
        if (raycastManager == null)
        {
            return false;
        }

        if (!raycastManager.Raycast(
            screenPoint,
            raycastHits,
            TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated))
        {
            return false;
        }

        pose = raycastHits[0].pose;
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

    private void RecordDetectedPayload(string payload, bool hasTrackedPose)
    {
        lastPayload = payload;
        lastPayloadTime = Time.unscaledTime;
        lastPayloadHasTrackedPose = hasTrackedPose;
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
