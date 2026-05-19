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
    [SerializeField] private Transform spawnPoseSource;
    [SerializeField] private float scanIntervalSeconds = 0.25f;
    [SerializeField] private int downsampleWidth = 640;

    private readonly BarcodeReader barcodeReader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            PossibleFormats = new[] { BarcodeFormat.QR_CODE }
        }
    };

    private Coroutine scanCoroutine;

    public override bool IsSupported => cameraManager != null;

    private void Awake()
    {
        if (cameraManager == null)
        {
            cameraManager = FindFirstObjectByType<ARCameraManager>();
        }
    }

    public override void StartScanning()
    {
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

            Color32[] pixels = new Color32[width * height];
            NativeArray<Color32>.Copy(buffer.Reinterpret<Color32>(1), pixels, pixels.Length);
            Result result = barcodeReader.Decode(pixels, width, height);
            if (result == null)
            {
                return;
            }

            Transform poseTransform = spawnPoseSource != null ? spawnPoseSource : cameraManager.transform;
            Vector3 position = poseTransform.position + poseTransform.forward;
            RaiseQrDetected(result.Text, new Pose(position, poseTransform.rotation));
        }
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
