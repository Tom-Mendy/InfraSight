#if INFRASIGHT_ANDROID_AR
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public sealed class AndroidArXrLifecycleController : MonoBehaviour
{
    private XRManagerSettings manager;
    private Coroutine startupRoutine;
    private bool startedSubsystems;
#if UNITY_ANDROID
    private bool cameraPermissionGranted;
    private bool permissionRequestComplete;
    private PermissionCallbacks permissionCallbacks;
#endif

    private void OnEnable()
    {
        manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        StartSubsystemsIfNeeded();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
#if UNITY_ANDROID
        if (hasFocus
            && !startedSubsystems
            && startupRoutine == null
            && Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            StartSubsystemsIfNeeded();
        }
#endif
    }

    private void StartSubsystemsIfNeeded()
    {
        if (manager != null && !startedSubsystems && startupRoutine == null)
        {
            startupRoutine = StartCoroutine(StartSubsystemsWhenInitialized());
        }
    }

    private IEnumerator StartSubsystemsWhenInitialized()
    {
        while (enabled && manager != null && !manager.isInitializationComplete)
        {
            yield return null;
        }

        if (!enabled || manager == null || !manager.isInitializationComplete || manager.activeLoader == null)
        {
            startupRoutine = null;
            yield break;
        }

#if UNITY_ANDROID
        yield return WaitForCameraPermission();
        if (!cameraPermissionGranted)
        {
            Debug.LogWarning("Camera permission denied; InfraSight Android AR scanning is disabled.");
            startupRoutine = null;
            yield break;
        }
#endif

        manager.StartSubsystems();
        startedSubsystems = true;
        startupRoutine = null;
        Debug.Log("Camera permission granted; InfraSight Android AR subsystems started.");
    }

#if UNITY_ANDROID
    private IEnumerator WaitForCameraPermission()
    {
        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            cameraPermissionGranted = true;
            yield break;
        }

        cameraPermissionGranted = false;
        permissionRequestComplete = false;

        permissionCallbacks = new PermissionCallbacks();
        permissionCallbacks.PermissionGranted += OnPermissionGranted;
        permissionCallbacks.PermissionDenied += OnPermissionDenied;

        Permission.RequestUserPermission(Permission.Camera, permissionCallbacks);
        while (enabled && !permissionRequestComplete)
        {
            yield return null;
        }

        ReleasePermissionCallbacks();
    }

    private void OnPermissionGranted(string permissionName)
    {
        if (permissionName != Permission.Camera)
        {
            return;
        }

        cameraPermissionGranted = true;
        permissionRequestComplete = true;
    }

    private void OnPermissionDenied(string permissionName)
    {
        if (permissionName != Permission.Camera)
        {
            return;
        }

        cameraPermissionGranted = false;
        permissionRequestComplete = true;
    }

    private void ReleasePermissionCallbacks()
    {
        if (permissionCallbacks == null)
        {
            return;
        }

        permissionCallbacks.PermissionGranted -= OnPermissionGranted;
        permissionCallbacks.PermissionDenied -= OnPermissionDenied;
        permissionCallbacks = null;
    }
#endif

    private void OnDisable()
    {
        if (startupRoutine != null)
        {
            StopCoroutine(startupRoutine);
            startupRoutine = null;
        }

#if UNITY_ANDROID
        ReleasePermissionCallbacks();
#endif

        if (!startedSubsystems || manager == null || !manager.isInitializationComplete)
        {
            return;
        }

        manager.StopSubsystems();
        startedSubsystems = false;
    }
}
#else
using UnityEngine;

public sealed class AndroidArXrLifecycleController : MonoBehaviour
{
}
#endif
