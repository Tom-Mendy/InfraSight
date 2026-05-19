#if INFRASIGHT_ANDROID_AR
using UnityEngine;

public static class AndroidArInfraSightBootstrapper
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Object.FindFirstObjectByType<InfraSightQrClient>() != null)
        {
            return;
        }

        var root = new GameObject("InfraSight Android AR Runtime");
        root.AddComponent<AndroidArQrScanProvider>();
        root.AddComponent<AndroidArStartupDiagnostics>();
        root.AddComponent<InfraSightQrClient>();
        Object.DontDestroyOnLoad(root);
    }
}
#endif
