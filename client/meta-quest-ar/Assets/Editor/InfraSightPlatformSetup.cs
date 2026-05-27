#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InfraSightPlatformSetup
{
    private const string MetaQuestDefine = "INFRASIGHT_META_QUEST";
    private const string AndroidArDefine = "INFRASIGHT_ANDROID_AR";
    private const string AndroidArScenePath = "Assets/Scenes/AndroidScene.unity";
    private const string VisualsPrefabRoot = "Packages/com.infrasight.client-visuals/Runtime/Resources/InfraSight";
    private const string XrSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

    [MenuItem("InfraSight/Platform/Configure Meta Quest")]
    public static void ConfigureMetaQuest()
    {
        SetAndroidPlatformDefines(MetaQuestDefine);
    }

    [MenuItem("InfraSight/Platform/Configure Android AR")]
    public static void ConfigureAndroidAr()
    {
        SetAndroidPlatformDefines(AndroidArDefine);
        SetAndroidXrAutomaticStartup(true);
    }

    [MenuItem("InfraSight/Platform/Create Android AR Scene")]
    public static void CreateAndroidArScene()
    {
        Type arSessionType = Type.GetType("UnityEngine.XR.ARFoundation.ARSession, Unity.XR.ARFoundation");
        Type arCameraManagerType = Type.GetType("UnityEngine.XR.ARFoundation.ARCameraManager, Unity.XR.ARFoundation");
        Type arCameraBackgroundType = Type.GetType("UnityEngine.XR.ARFoundation.ARCameraBackground, Unity.XR.ARFoundation");
        Type xrOriginType = Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils");

        if (arSessionType == null || arCameraManagerType == null || arCameraBackgroundType == null || xrOriginType == null)
        {
            Debug.LogWarning("AR Foundation/ARCore packages must be resolved before creating the Android AR scene.");
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "AndroidScene";

        var arSession = new GameObject("AR Session");
        arSession.AddComponent(arSessionType);

        var xrOrigin = new GameObject("XR Origin");
        Component xrOriginComponent = xrOrigin.AddComponent(xrOriginType);

        var cameraOffset = new GameObject("Camera Offset");
        cameraOffset.transform.SetParent(xrOrigin.transform);

        var mainCamera = new GameObject("Main Camera");
        mainCamera.tag = "MainCamera";
        mainCamera.transform.SetParent(cameraOffset.transform);
        Camera camera = mainCamera.AddComponent<Camera>();
        mainCamera.AddComponent<AudioListener>();
        Component cameraManager = mainCamera.AddComponent(arCameraManagerType);
        mainCamera.AddComponent(arCameraBackgroundType);
        SetXrOriginReferences(xrOriginComponent, camera, cameraOffset);

        var clientRoot = new GameObject("InfraSight QR Client");
        var qrProvider = clientRoot.AddComponent<AndroidArQrScanProvider>();
        var qrClient = clientRoot.AddComponent<InfraSightQrClient>();
        var diagnostics = clientRoot.AddComponent<AndroidArStartupDiagnostics>();
        SetObjectReference(qrProvider, "cameraManager", cameraManager);
        SetObjectReference(qrProvider, "spawnPoseSource", mainCamera.transform);
        SetObjectReference(qrClient, "qrScanProvider", qrProvider);
        SetObjectReference(qrClient, "spawnSpherePrefab", LoadVisualPrefab("Sphere.prefab"));
        SetObjectReference(qrClient, "spawnCubePrefab", LoadVisualPrefab("Cube.prefab"));
        SetObjectReference(qrClient, "machineVisualizationPrefab", LoadVisualPrefab("DeviceInfo.prefab"));
        SetObjectReference(qrClient, "feedbackPrefab", LoadVisualPrefab("FeedBack.prefab"));
        SetObjectReference(diagnostics, "cameraManager", cameraManager);

        EditorSceneManager.SaveScene(scene, AndroidArScenePath);
        SetAndroidPlatformDefines(AndroidArDefine);
        SetAndroidXrAutomaticStartup(true);
        Debug.Log($"InfraSight Android AR scene created at {AndroidArScenePath}");
    }

    private static void SetAndroidPlatformDefines(string selectedDefine)
    {
        var target = UnityEditor.Build.NamedBuildTarget.Android;
        string current = PlayerSettings.GetScriptingDefineSymbols(target);
        string[] defines = current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new System.Collections.Generic.List<string>();

        foreach (string define in defines)
        {
            if (define == MetaQuestDefine || define == AndroidArDefine)
            {
                continue;
            }

            result.Add(define);
        }

        result.Add(selectedDefine);
        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", result));
        Debug.Log($"InfraSight Android platform configured: {selectedDefine}");
    }

    private static void SetAndroidXrAutomaticStartup(bool enabled)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(XrSettingsPath);
        foreach (UnityEngine.Object asset in assets)
        {
            if (asset == null || asset.name != "Android Providers")
            {
                continue;
            }

            var serializedObject = new SerializedObject(asset);
            serializedObject.FindProperty("m_AutomaticLoading").boolValue = enabled;
            serializedObject.FindProperty("m_AutomaticRunning").boolValue = enabled;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"InfraSight Android XR automatic startup set to {enabled}");
            return;
        }

        Debug.LogWarning($"Android Providers asset not found in {XrSettingsPath}");
    }

    private static GameObject LoadPrefab(string path)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static GameObject LoadVisualPrefab(string fileName)
    {
        return LoadPrefab($"{VisualsPrefabRoot}/{fileName}");
    }

    private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        var serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning($"Missing serialized property {propertyName} on {target.name}");
            return;
        }

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetXrOriginReferences(UnityEngine.Object target, Camera camera, GameObject cameraOffset)
    {
        var serializedObject = new SerializedObject(target);
        SerializedProperty cameraProperty = serializedObject.FindProperty("m_Camera");
        SerializedProperty offsetProperty = serializedObject.FindProperty("m_CameraFloorOffsetObject");

        if (cameraProperty != null)
        {
            cameraProperty.objectReferenceValue = camera;
        }

        if (offsetProperty != null)
        {
            offsetProperty.objectReferenceValue = cameraOffset;
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
