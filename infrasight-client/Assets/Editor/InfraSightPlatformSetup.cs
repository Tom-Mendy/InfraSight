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

    [MenuItem("InfraSight/Platform/Configure Meta Quest")]
    public static void ConfigureMetaQuest()
    {
        SetAndroidPlatformDefines(MetaQuestDefine);
    }

    [MenuItem("InfraSight/Platform/Configure Android AR")]
    public static void ConfigureAndroidAr()
    {
        SetAndroidPlatformDefines(AndroidArDefine);
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

    private static GameObject LoadPrefab(string path)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
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
}
#endif
