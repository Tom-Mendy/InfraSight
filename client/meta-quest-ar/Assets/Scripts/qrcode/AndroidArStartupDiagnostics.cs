#if INFRASIGHT_ANDROID_AR
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class AndroidArStartupDiagnostics : MonoBehaviour
{
    [SerializeField] private ARCameraManager cameraManager;

    private IEnumerator Start()
    {
        if (cameraManager == null)
        {
            cameraManager = FindFirstObjectByType<ARCameraManager>();
        }

        LogState("startup");
        yield return null;
        LogState("after-first-frame");
        yield return new WaitForSeconds(2f);
        LogState("after-2s");
    }

    private void LogState(string phase)
    {
        string loaderName = "none";
        XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (manager != null && manager.activeLoader != null)
        {
            loaderName = manager.activeLoader.name;
        }

        bool hasCameraPermission = true;
#if UNITY_ANDROID
        hasCameraPermission = Permission.HasUserAuthorizedPermission(Permission.Camera);
#endif

        Debug.Log(
            $"InfraSight Android AR diagnostics [{phase}] "
            + $"platform={Application.platform}, "
            + $"device='{SystemInfo.deviceModel}', "
            + $"cameraPermission={hasCameraPermission}, "
            + $"arSessionState={ARSession.state}, "
            + $"xrLoader='{loaderName}', "
            + $"xrAutomaticLoading={manager?.automaticLoading}, "
            + $"xrAutomaticRunning={manager?.automaticRunning}, "
            + $"cameraManagerEnabled={(cameraManager != null && cameraManager.enabled)}");
    }
}
#else
using UnityEngine;

public class AndroidArStartupDiagnostics : MonoBehaviour
{
}
#endif
