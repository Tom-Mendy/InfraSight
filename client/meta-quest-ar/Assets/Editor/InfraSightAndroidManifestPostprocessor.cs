#if UNITY_EDITOR
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class InfraSightAndroidManifestPostprocessor : IPostGenerateGradleAndroidProject, IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != UnityEditor.BuildTarget.Android)
        {
            return;
        }

        string defines = GetAndroidDefines();
        bool metaQuest = defines.Contains("INFRASIGHT_META_QUEST");
        bool androidAr = defines.Contains("INFRASIGHT_ANDROID_AR");
        if (metaQuest == androidAr)
        {
            UnityEngine.Debug.LogWarning("InfraSight Android build should define exactly one platform: INFRASIGHT_META_QUEST or INFRASIGHT_ANDROID_AR.");
        }
    }

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string defines = GetAndroidDefines();
        if (defines.Contains("INFRASIGHT_ANDROID_AR"))
        {
            ApplyTemplate(path, "AndroidManifest.AndroidAR.xml");
            return;
        }

        if (defines.Contains("INFRASIGHT_META_QUEST"))
        {
            ApplyTemplate(path, "AndroidManifest.MetaQuest.xml");
        }
    }

    private static string GetAndroidDefines()
    {
        NamedBuildTarget target = NamedBuildTarget.Android;
        return PlayerSettings.GetScriptingDefineSymbols(target);
    }

    private static void ApplyTemplate(string gradleProjectPath, string templateName)
    {
        string source = Path.Combine("Assets", "Plugins", "Android", templateName);
        string target = Path.Combine(gradleProjectPath, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(source) || !File.Exists(target))
        {
            UnityEngine.Debug.LogWarning($"InfraSight manifest template missing: {source} or {target}");
            return;
        }

        var document = new XmlDocument { PreserveWhitespace = true };
        document.Load(source);
        document.Save(target);
        UnityEngine.Debug.Log($"InfraSight Android manifest applied: {templateName}");
    }
}
#endif
