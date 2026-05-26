#if INFRASIGHT_ANDROID_AR
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

public sealed class AndroidArXrLifecycleController : MonoBehaviour
{
    private XRManagerSettings manager;
    private Coroutine startupRoutine;
    private bool startedSubsystems;

    private void OnEnable()
    {
        manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (manager != null && !startedSubsystems)
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

        startupRoutine = null;
        if (!enabled || manager == null || !manager.isInitializationComplete || manager.activeLoader == null)
        {
            yield break;
        }

        manager.StartSubsystems();
        startedSubsystems = true;
    }

    private void OnDisable()
    {
        if (startupRoutine != null)
        {
            StopCoroutine(startupRoutine);
            startupRoutine = null;
        }

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
